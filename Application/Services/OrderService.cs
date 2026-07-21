using System.Data;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public class OrderService : IOrderService
    {
        private readonly IGenericRepository<Order> _orderRepo;
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IPaymentProviderResolver _paymentProviders;
        private readonly IOutboxWriter _outbox;
        private readonly IMapper _mapper;
        private readonly TimeProvider _timeProvider;
        private readonly OrderLifecycleOptions _lifecycleOptions;
        private readonly IAuditWriter _audit;

        public OrderService(
            IGenericRepository<Order> orderRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPaymentProviderResolver paymentProviders,
            IOutboxWriter outbox,
            IMapper mapper)
            : this(
                orderRepo,
                context,
                consistency,
                paymentProviders,
                outbox,
                mapper,
                TimeProvider.System,
                Options.Create(new OrderLifecycleOptions()))
        {
        }

        public OrderService(
            IGenericRepository<Order> orderRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPaymentProviderResolver paymentProviders,
            IOutboxWriter outbox,
            IMapper mapper,
            TimeProvider timeProvider)
            : this(
                orderRepo,
                context,
                consistency,
                paymentProviders,
                outbox,
                mapper,
                timeProvider,
                Options.Create(new OrderLifecycleOptions()))
        {
        }

        public OrderService(
            IGenericRepository<Order> orderRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPaymentProviderResolver paymentProviders,
            IOutboxWriter outbox,
            IMapper mapper,
            TimeProvider timeProvider,
            IOptions<OrderLifecycleOptions> lifecycleOptions,
            IAuditWriter? auditWriter = null)
        {
            _orderRepo = orderRepo;
            _context = context;
            _consistency = consistency;
            _paymentProviders = paymentProviders;
            _outbox = outbox;
            _mapper = mapper;
            _timeProvider = timeProvider;
            _lifecycleOptions = lifecycleOptions.Value;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<OrderResponse> PlaceOrderAsync(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
            var requestHash = HashCheckoutRequest(request);
            var existingOrder = await FindIdempotentOrderAsync(
                userId,
                normalizedKey,
                cancellationToken);

            if (existingOrder != null)
            {
                EnsureSameIdempotencyRequest(existingOrder, requestHash);
                return await GetByIdAsync(existingOrder.Id, userId, true, cancellationToken);
            }

            Guid orderId;
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var cart = await _consistency.LockCartByUserIdAsync(userId, cancellationToken)
                    ?? throw new BusinessException("Không tìm thấy giỏ hàng.");

                existingOrder = await FindIdempotentOrderAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (existingOrder != null)
                {
                    EnsureSameIdempotencyRequest(existingOrder, requestHash);
                    orderId = existingOrder.Id;
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return await GetByIdAsync(orderId, userId, true, cancellationToken);
                }

                var pendingOrderCount = await _context.Orders
                    .CountAsync(order => order.UserId == userId
                        && order.Status == OrderStatus.Pending, cancellationToken);
                if (pendingOrderCount >= _lifecycleOptions.MaxPendingOrdersPerCustomer)
                {
                    throw new ConflictException(
                        "pending_order_limit_reached",
                        $"Bạn chỉ có thể có tối đa {_lifecycleOptions.MaxPendingOrdersPerCustomer} đơn hàng đang chờ xử lý.");
                }

                var productIds = await _context.CartItems
                    .AsNoTracking()
                    .Where(item => item.CartId == cart.Id)
                    .Select(item => item.ProductId)
                    .ToListAsync(cancellationToken);

                if (productIds.Count == 0)
                    throw new BusinessException("Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi đặt hàng.");

                var products = await LoadProductsForUpdateAsync(productIds, cancellationToken);
                await _context.Entry(cart)
                    .Collection(candidate => candidate.CartItems)
                    .LoadAsync(cancellationToken);

                if (cart.CartItems.Count == 0)
                    throw new BusinessException("Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi đặt hàng.");

                foreach (var item in cart.CartItems)
                {
                    if (!products.TryGetValue(item.ProductId, out var product))
                    {
                        throw new BusinessException(
                            "Cart product data is no longer available.");
                    }

                    item.Product = product;
                }

                foreach (var item in cart.CartItems)
                {
                    DomainRuleGuard.AsBusiness(() =>
                        InventoryPolicy.EnsureCanReserve(item.Product!, item.Quantity));
                }

                var orderOccurredAt = UtcNow;
                var subtotal = DomainRuleGuard.AsBusiness(() =>
                    OrderPricingPolicy.CalculateSubtotal(cart.CartItems.Select(item =>
                        new OrderPricingLine(
                            item.Product?.Name ?? string.Empty,
                            item.Product?.Price ?? 0,
                            item.Quantity))));
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OrderNumber = CreateOrderNumber(orderOccurredAt),
                    IdempotencyKey = normalizedKey,
                    IdempotencyRequestHash = requestHash,
                    OrderDate = orderOccurredAt,
                    ShippingAddress = request.ShippingAddress.Trim(),
                    Note = NormalizeOptional(request.Note)
                };
                DomainRuleGuard.AsBusiness(() =>
                    order.SetPricing(subtotal, discount: 0, shipping: 0, tax: 0));
                DomainRuleGuard.AsBusiness(() => order.SetPendingExpiration(
                    orderOccurredAt.AddMinutes(_lifecycleOptions.PendingCodHoldMinutes)));
                orderId = order.Id;
                var paymentProvider = _paymentProviders.GetCheckoutProvider(request.PaymentMethod);
                var initializedPayment = PaymentProviderContract.NormalizeInitialization(
                    paymentProvider,
                    paymentProvider.Initialize(new PaymentInitializationRequest(
                        order.Id,
                        order.OrderNumber,
                        order.TotalAmount)));

                var paymentCreatedAt = orderOccurredAt;
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Method = request.PaymentMethod,
                    Amount = order.TotalAmount,
                    Provider = initializedPayment.Provider,
                    ProviderTransactionId = initializedPayment.ProviderTransactionId,
                    CreatedAt = paymentCreatedAt
                };
                if (initializedPayment.Status != payment.Status)
                {
                    DomainRuleGuard.AsBusiness(() =>
                        payment.ChangeStatus(initializedPayment.Status, paymentCreatedAt));
                }

                _context.Orders.Add(order);
                _context.Payments.Add(payment);
                _context.PaymentStatusHistories.Add(new PaymentStatusHistory
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    ChangedByUserId = userId,
                    FromStatus = null,
                    ToStatus = payment.Status,
                    Source = PaymentStatusChangeSource.Checkout,
                    Reference = order.OrderNumber,
                    OccurredAt = paymentCreatedAt,
                    CreatedAt = paymentCreatedAt
                });
                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ChangedByUserId = userId,
                    FromStatus = null,
                    ToStatus = order.Status,
                    Note = "Order placed; inventory reserved",
                    CreatedAt = orderOccurredAt
                });

                foreach (var item in cart.CartItems)
                {
                    var product = item.Product!;
                    var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                        InventoryPolicy.Reserve(product, item.Quantity));

                    _context.OrderDetails.Add(new OrderDetail
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        ProductId = item.ProductId,
                        ProductNameSnapshot = product.Name,
                        Quantity = item.Quantity,
                        UnitPrice = product.Price
                    });
                    _context.InventoryTransactions.Add(new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        OrderId = order.Id,
                        CreatedByUserId = userId,
                        Type = InventoryTransactionType.OrderPlaced,
                        QuantityChange = inventoryMutation.QuantityChange,
                        BalanceAfter = inventoryMutation.BalanceAfter,
                        Reason = $"Order {order.OrderNumber}",
                        CreatedAt = orderOccurredAt
                    });
                    _context.CartItems.Remove(item);
                }

                _outbox.EnqueueNotification(
                    userId,
                    "Đặt hàng thành công",
                    $"Đơn hàng {order.OrderNumber} đã được tiếp nhận và đang chờ xác nhận.",
                    order.Id);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Tồn kho vừa được thay đổi. Vui lòng tải lại giỏ hàng và thử lại.",
                    ex);
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                var savedOrder = await FindIdempotentOrderAsync(userId, normalizedKey, cancellationToken);
                if (savedOrder != null)
                {
                    EnsureSameIdempotencyRequest(savedOrder, requestHash);
                    return await GetByIdAsync(savedOrder.Id, userId, true, cancellationToken);
                }

                throw new ConflictException("Không thể tạo đơn hàng do dữ liệu vừa được cập nhật.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng sản phẩm. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(orderId, userId, true, cancellationToken);
        }

        public async Task<PagedResult<OrderResponse>> GetMyOrdersAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(page, pageSize);
            var query = BuildOrderQuery()
                .Where(order => order.UserId == userId)
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .ToListAsync(cancellationToken);

            return PagedResult<OrderResponse>.Create(
                _mapper.Map<IEnumerable<OrderResponse>>(items),
                totalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<OrderResponse> GetByIdAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
        {
            var query = BuildOrderQuery().Where(order => order.Id == orderId);
            if (!canProcessOrders)
                query = query.Where(order => order.UserId == userId);

            var order = await query.FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

            return _mapper.Map<OrderResponse>(order);
        }

        public async Task<PagedResult<OrderResponse>> GetAllOrdersAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var query = BuildOrderQuery();

            if (queryParams.Status.HasValue)
                query = query.Where(order => order.Status == queryParams.Status.Value);

            if (queryParams.UserId.HasValue)
                query = query.Where(order => order.UserId == queryParams.UserId.Value);

            var orderedQuery = query
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await orderedQuery.CountAsync(cancellationToken);
            var items = await orderedQuery
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .ToListAsync(cancellationToken);

            return PagedResult<OrderResponse>.Create(
                _mapper.Map<IEnumerable<OrderResponse>>(items),
                totalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<OrderResponse> UpdateStatusAsync(
            Guid orderId,
            Guid actorUserId,
            UpdateOrderStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(orderId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

                if (order.Status != request.Status)
                {
                    var payment = await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken);
                    var occurredAt = UtcNow;
                    var statusChange = request.Status == OrderStatus.Cancelled
                        ? DomainRuleGuard.AsBusiness(() => order.Cancel(
                            occurredAt,
                            payment?.Status,
                            NormalizeOptional(request.Note) ?? "CancelledByStaff"))
                        : DomainRuleGuard.AsBusiness(() =>
                            order.ChangeStatus(request.Status, payment?.Status));

                    if (request.Status == OrderStatus.Cancelled)
                    {
                        await RestoreOrderStockAsync(order, actorUserId, occurredAt, cancellationToken);
                    }

                    UpdatePaymentForOrderStatus(
                        payment,
                        request.Status,
                        actorUserId,
                        occurredAt);

                    _context.OrderStatusHistories.Add(new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        ChangedByUserId = actorUserId,
                        FromStatus = statusChange.Previous,
                        ToStatus = request.Status,
                        Note = NormalizeOptional(request.Note),
                        CreatedAt = occurredAt
                    });

                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Cập nhật trạng thái đơn hàng",
                        $"Đơn hàng {order.OrderNumber} đã chuyển sang trạng thái {request.Status}.",
                        order.Id,
                        payment?.Id);

                    _audit.Write(
                        "order.status.update",
                        "Order",
                        order.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["fromStatus"] = statusChange.Previous.ToString(),
                            ["toStatus"] = request.Status.ToString()
                        });

                    await _context.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Đơn hàng hoặc tồn kho vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng hoặc sản phẩm. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(orderId, actorUserId, true, cancellationToken);
        }

        public async Task<OrderResponse> CancelByCustomerAsync(
            Guid orderId,
            Guid customerUserId,
            CancelOrderRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(orderId, cancellationToken);
                if (order == null || order.UserId != customerUserId)
                    throw new NotFoundException("Không tìm thấy đơn hàng.");

                if (order.Status == OrderStatus.Cancelled)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return await GetByIdAsync(orderId, customerUserId, false, cancellationToken);
                }

                if (order.Status != OrderStatus.Pending)
                {
                    throw new ConflictException(
                        "customer_order_cancellation_forbidden",
                        "Khách hàng chỉ có thể hủy đơn hàng đang chờ xử lý.");
                }

                var payment = await _consistency.LockPaymentByOrderIdAsync(order.Id, cancellationToken);
                var occurredAt = UtcNow;
                var statusChange = DomainRuleGuard.AsConflict(() => order.Cancel(
                    occurredAt,
                    payment?.Status,
                    NormalizeOptional(request.Reason) ?? "CancelledByCustomer"));

                await RestoreOrderStockAsync(order, customerUserId, occurredAt, cancellationToken);
                UpdatePaymentForOrderStatus(payment, OrderStatus.Cancelled, customerUserId, occurredAt);
                AddCancellationHistory(order, statusChange, customerUserId, request.Reason, occurredAt);
                EnqueueCancellationNotification(order, payment, "Đơn hàng đã được hủy theo yêu cầu của bạn.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Đơn hàng hoặc tồn kho vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(orderId, customerUserId, false, cancellationToken);
        }

        public async Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
                return Array.Empty<Guid>();

            return await _context.Orders
                .AsNoTracking()
                .Where(order => order.Status == OrderStatus.Pending
                    && order.ExpiresAt != null
                    && order.ExpiresAt <= asOf)
                .OrderBy(order => order.ExpiresAt)
                .ThenBy(order => order.Id)
                .Select(order => order.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> ExpirePendingOrderAsync(
            Guid orderId,
            DateTime asOf,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(orderId, cancellationToken);
                if (order == null
                    || order.Status != OrderStatus.Pending
                    || !order.ExpiresAt.HasValue
                    || order.ExpiresAt.Value > asOf)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return false;
                }

                var payment = await _consistency.LockPaymentByOrderIdAsync(order.Id, cancellationToken);
                var statusChange = DomainRuleGuard.AsConflict(() => order.Cancel(
                    asOf,
                    payment?.Status,
                    "SystemExpired",
                    isExpiration: true));

                await RestoreOrderStockAsync(order, null, asOf, cancellationToken);
                UpdatePaymentForOrderStatus(payment, OrderStatus.Cancelled, null, asOf);
                AddCancellationHistory(order, statusChange, null, "SystemExpired", asOf);
                EnqueueCancellationNotification(order, payment, "Đơn hàng đã hết thời gian giữ tồn kho và được hủy tự động.");

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                return true;
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private IQueryable<Order> BuildOrderQuery()
            => _orderRepo.Query()
                .AsNoTracking()
                .Include(order => order.OrderDetails)
                .Include(order => order.Payment)
                    .ThenInclude(payment => payment!.StatusHistory.OrderBy(history => history.CreatedAt))
                .Include(order => order.StatusHistory.OrderBy(history => history.CreatedAt))
                .AsSplitQuery();

        private async Task<Order?> FindIdempotentOrderAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken)
            => await _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(order => order.UserId == userId
                    && order.IdempotencyKey == idempotencyKey, cancellationToken);

        private async Task<Dictionary<Guid, Product>> LoadProductsForUpdateAsync(
            IEnumerable<Guid> productIds,
            CancellationToken cancellationToken)
        {
            var products = new Dictionary<Guid, Product>();
            foreach (var productId in productIds.Distinct().OrderBy(id => id))
            {
                var product = await _consistency.LockProductAsync(
                        productId,
                        activeOnly: false,
                        cancellationToken)
                    ?? throw new BusinessException("Dữ liệu sản phẩm của giỏ hàng hoặc đơn hàng không còn tồn tại.");

                products.Add(productId, product);
            }

            return products;
        }

        private async Task RestoreOrderStockAsync(
            Order order,
            Guid? actorUserId,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            await _context.Entry(order)
                .Collection(candidate => candidate.OrderDetails)
                .LoadAsync(cancellationToken);
            var products = await LoadProductsForUpdateAsync(
                order.OrderDetails.Select(detail => detail.ProductId),
                cancellationToken);

            foreach (var detail in order.OrderDetails)
            {
                var product = products[detail.ProductId];
                var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.Release(product, detail.Quantity));

                _context.InventoryTransactions.Add(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    OrderId = order.Id,
                    CreatedByUserId = actorUserId,
                    Type = InventoryTransactionType.OrderCancelled,
                    QuantityChange = inventoryMutation.QuantityChange,
                    BalanceAfter = inventoryMutation.BalanceAfter,
                    Reason = $"Cancelled order {order.OrderNumber}",
                    CreatedAt = occurredAt
                });
            }
        }

        private void UpdatePaymentForOrderStatus(
            Payment? payment,
            OrderStatus orderStatus,
            Guid? actorUserId,
            DateTime occurredAt)
        {
            if (payment == null)
                return;

            PaymentStatus? nextStatus = null;
            if (orderStatus == OrderStatus.Cancelled && payment.Status == PaymentStatus.Pending)
                nextStatus = PaymentStatus.Cancelled;
            else if (orderStatus == OrderStatus.Delivered
                && payment.Method == PaymentMethod.CashOnDelivery
                && payment.Status == PaymentStatus.Pending)
                nextStatus = PaymentStatus.Paid;

            if (!nextStatus.HasValue)
                return;

            var statusChange = DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(nextStatus.Value, occurredAt));

            _context.PaymentStatusHistories.Add(new PaymentStatusHistory
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                ChangedByUserId = actorUserId,
                FromStatus = statusChange.Previous,
                ToStatus = nextStatus.Value,
                Source = PaymentStatusChangeSource.OrderLifecycle,
                Reference = orderStatus.ToString(),
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            });
        }

        private void AddCancellationHistory(
            Order order,
            StatusChange<OrderStatus> statusChange,
            Guid? actorUserId,
            string? note,
            DateTime occurredAt)
        {
            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ChangedByUserId = actorUserId,
                FromStatus = statusChange.Previous,
                ToStatus = OrderStatus.Cancelled,
                Note = NormalizeOptional(note),
                CreatedAt = occurredAt
            });
        }

        private void EnqueueCancellationNotification(
            Order order,
            Payment? payment,
            string message)
        {
            _outbox.EnqueueNotification(
                order.UserId,
                "Cập nhật trạng thái đơn hàng",
                $"Đơn hàng {order.OrderNumber}: {message}",
                order.Id,
                payment?.Id);
        }

        private static string NormalizeIdempotencyKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new BusinessException("Header Idempotency-Key là bắt buộc khi đặt hàng.");

            var normalized = value.Trim();
            if (normalized.Length > 100)
                throw new BusinessException("Header Idempotency-Key không được vượt quá 100 ký tự.");

            return normalized;
        }

        private static string HashCheckoutRequest(PlaceOrderRequest request)
        {
            var canonical = string.Join('\n',
                request.ShippingAddress.Trim(),
                NormalizeOptional(request.Note) ?? string.Empty,
                ((int)request.PaymentMethod).ToString());
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        private static void EnsureSameIdempotencyRequest(Order order, string requestHash)
        {
            if (!string.Equals(order.IdempotencyRequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "Idempotency-Key đã được sử dụng cho một yêu cầu đặt hàng khác.");
            }
        }

        private static string CreateOrderNumber(DateTime occurredAt)
            => $"ORD-{occurredAt:yyyyMMdd}-{Guid.NewGuid():N}"[..32].ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    }
}
