using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderCheckoutUseCase
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly ICartRepository _cartRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IPaymentProviderResolver _paymentProviders;
        private readonly IOutboxWriter _outbox;
        private readonly OrderPricingUseCase _pricing;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;
        private readonly OrderLifecycleOptions _options;

        public OrderCheckoutUseCase(
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            ICartRepository cartRepository,
            IInventoryRepository inventoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IPaymentProviderResolver paymentProviders,
            IOutboxWriter outbox,
            OrderPricingUseCase pricing,
            OrderQueryUseCase queries,
            TimeProvider timeProvider,
            IOptions<OrderLifecycleOptions> options)
        {
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _cartRepository = cartRepository;
            _inventoryRepository = inventoryRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _paymentProviders = paymentProviders;
            _outbox = outbox;
            _pricing = pricing;
            _queries = queries;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            var paymentMethod = Enum.IsDefined(request.PaymentMethod)
                ? request.PaymentMethod.ToString()
                : "unknown";
            using var telemetry = BusinessTelemetry.Start(
                "checkout.place_order",
                cancellationToken,
                new KeyValuePair<string, object?>(
                    "payment.method",
                    paymentMethod));
            var normalizedKey = NormalizeIdempotencyKey(idempotencyKey);
            var requestHash = HashRequest(request);
            var existingOrder = await FindIdempotentOrderAsync(
                userId,
                normalizedKey,
                cancellationToken);
            if (existingOrder != null)
            {
                EnsureSameRequest(existingOrder, requestHash);
                telemetry.SetTag("checkout.idempotency.replay", true);
                var replay = await GetResponseAsync(
                    existingOrder.Id,
                    userId,
                    cancellationToken);
                telemetry.Complete();
                return replay;
            }

            Guid orderId;
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;
            try
            {
                var cart = await _consistency.LockCartByUserIdAsync(
                    userId,
                    cancellationToken)
                    ?? throw new BusinessException("Không tìm thấy giỏ hàng.");
                existingOrder = await FindIdempotentOrderAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (existingOrder != null)
                {
                    EnsureSameRequest(existingOrder, requestHash);
                    orderId = existingOrder.Id;
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    telemetry.SetTag("checkout.idempotency.replay", true);
                    var replay = await GetResponseAsync(
                        orderId,
                        userId,
                        cancellationToken);
                    telemetry.Complete();
                    return replay;
                }

                var pendingOrderCount =
                    await _orderRepository.CountPendingByUserAsync(
                    userId,
                    cancellationToken);
                if (pendingOrderCount
                    >= _options.MaxPendingOrdersPerCustomer)
                {
                    throw new ConflictException(
                        "pending_order_limit_reached",
                        $"Bạn chỉ có thể có tối đa "
                        + $"{_options.MaxPendingOrdersPerCustomer} "
                        + "đơn hàng đang chờ xử lý.");
                }

                var productIds = await _cartRepository.GetProductIdsAsync(
                    cart.Id,
                    cancellationToken);
                if (productIds.Count == 0)
                {
                    throw new BusinessException(
                        "Giỏ hàng trống. Vui lòng thêm sản phẩm "
                        + "trước khi đặt hàng.");
                }

                var products = await LoadProductsForUpdateAsync(
                    productIds,
                    cancellationToken);
                await _cartRepository.LoadItemsAsync(
                    cart,
                    cancellationToken);
                if (cart.CartItems.Count == 0)
                {
                    throw new BusinessException(
                        "Giỏ hàng trống. Vui lòng thêm sản phẩm "
                        + "trước khi đặt hàng.");
                }

                foreach (var item in cart.CartItems)
                {
                    if (!products.TryGetValue(
                        item.ProductId,
                        out var product))
                    {
                        throw new BusinessException(
                            "Dữ liệu sản phẩm trong giỏ hàng "
                            + "không còn khả dụng.");
                    }
                    item.Product = product;
                    DomainRuleGuard.AsBusiness(() =>
                        InventoryPolicy.EnsureCanReserve(
                            product,
                            item.Quantity));
                }

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var pricing = await _pricing.CalculateForCheckoutAsync(
                    userId,
                    cart.CartItems,
                    request.PromotionCode,
                    request.ShippingMethod,
                    occurredAt,
                    cancellationToken);
                if (request.ExpectedTotalAmount.HasValue
                    && request.ExpectedTotalAmount.Value
                        != pricing.Amounts.Total)
                {
                    throw new ConflictException(
                        "checkout_price_changed",
                        "Tổng tiền đã thay đổi. Vui lòng tải lại báo giá trước khi đặt hàng.");
                }
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OrderNumber = CreateOrderNumber(occurredAt),
                    IdempotencyKey = normalizedKey,
                    IdempotencyRequestHash = requestHash,
                    PromotionId = pricing.Promotion?.Id,
                    PromotionCodeSnapshot = pricing.Promotion?.Code,
                    ShippingMethod = request.ShippingMethod,
                    Currency = pricing.Currency,
                    OrderDate = occurredAt,
                    ShippingAddress = request.ShippingAddress.Trim(),
                    Note = NormalizeOptional(request.Note)
                };
                DomainRuleGuard.AsBusiness(() =>
                    order.SetPricing(
                        pricing.Amounts.Subtotal,
                        pricing.Amounts.Discount,
                        pricing.Amounts.Shipping,
                        pricing.Amounts.Tax));
                DomainRuleGuard.AsBusiness(() =>
                    order.SetPendingExpiration(
                        occurredAt.AddMinutes(
                            _options.PendingCodHoldMinutes)));
                orderId = order.Id;
                var provider = _paymentProviders.GetCheckoutProvider(
                    request.PaymentMethod);
                var initialized = PaymentProviderContract
                    .NormalizeInitialization(
                        provider,
                        provider.Initialize(
                            new PaymentInitializationRequest(
                                order.Id,
                                order.OrderNumber,
                                order.TotalAmount)));
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Method = request.PaymentMethod,
                    Amount = order.TotalAmount,
                    Provider = initialized.Provider,
                    ProviderTransactionId =
                        initialized.ProviderTransactionId,
                    CreatedAt = occurredAt
                };
                if (initialized.Status != payment.Status)
                {
                    DomainRuleGuard.AsBusiness(() =>
                        payment.ChangeStatus(
                            initialized.Status,
                            occurredAt));
                }

                AddOrderRecords(
                    order,
                    payment,
                    cart,
                    userId,
                    occurredAt);
                await _pricing.RedeemAsync(
                    pricing,
                    order,
                    userId,
                    occurredAt,
                    cancellationToken);
                _outbox.EnqueueNotification(
                    userId,
                    "Đặt hàng thành công",
                    $"Đơn hàng {order.OrderNumber} đã được tiếp nhận "
                    + "và đang chờ xác nhận.",
                    order.Id);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException(
                    "Tồn kho vừa được thay đổi. Vui lòng tải lại "
                    + "giỏ hàng và thử lại.",
                    ex);
            }
            catch (Exception ex)
                when (_consistency.IsUniqueConstraintViolation(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                var savedOrder = await FindIdempotentOrderAsync(
                    userId,
                    normalizedKey,
                    cancellationToken);
                if (savedOrder != null)
                {
                    EnsureSameRequest(savedOrder, requestHash);
                    telemetry.SetTag(
                        "checkout.idempotency.replay",
                        true);
                    var replay = await GetResponseAsync(
                        savedOrder.Id,
                        userId,
                        cancellationToken);
                    telemetry.Complete();
                    return replay;
                }
                throw new ConflictException(
                    "Không thể tạo đơn hàng do dữ liệu "
                    + "vừa được cập nhật.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên "
                    + "cùng sản phẩm. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            var response = await GetResponseAsync(
                orderId,
                userId,
                cancellationToken);
            telemetry.Complete();
            return response;
        }

        private void AddOrderRecords(
            Order order,
            Payment payment,
            Cart cart,
            Guid userId,
            DateTime occurredAt)
        {
            _orderRepository.Add(order);
            _paymentRepository.Add(payment);
            _paymentRepository.AddStatusHistory(new PaymentStatusHistory
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                ChangedByUserId = userId,
                FromStatus = null,
                ToStatus = payment.Status,
                Source = PaymentStatusChangeSource.Checkout,
                Reference = order.OrderNumber,
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            });
            _orderRepository.AddStatusHistory(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ChangedByUserId = userId,
                FromStatus = null,
                ToStatus = order.Status,
                Note = "Đã đặt hàng và giữ tồn kho",
                CreatedAt = occurredAt
            });

            foreach (var item in cart.CartItems)
            {
                var product = item.Product!;
                var mutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.Reserve(product, item.Quantity));
                _orderRepository.AddDetail(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ProductId = item.ProductId,
                    ProductNameSnapshot = product.Name,
                    Quantity = item.Quantity,
                    UnitPrice = product.Price
                });
                _inventoryRepository.AddTransaction(
                    new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        OrderId = order.Id,
                        CreatedByUserId = userId,
                        Type = InventoryTransactionType.OrderPlaced,
                        QuantityChange = mutation.QuantityChange,
                        BalanceAfter = mutation.BalanceAfter,
                        Reason = $"Đặt đơn {order.OrderNumber}",
                        CreatedAt = occurredAt
                    });
                _cartRepository.RemoveItem(item);
            }
        }

        private async Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid userId,
            CancellationToken cancellationToken)
            => await _queries.GetByIdAsync(
                orderId,
                userId,
                canProcessOrders: true,
                cancellationToken);

        private async Task<Order?> FindIdempotentOrderAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken)
            => await _orderRepository.FindByIdempotencyKeyAsync(
                userId,
                idempotencyKey,
                cancellationToken);

        private async Task<Dictionary<Guid, Product>>
            LoadProductsForUpdateAsync(
                IEnumerable<Guid> productIds,
                CancellationToken cancellationToken)
        {
            var products = new Dictionary<Guid, Product>();
            foreach (var productId in productIds
                .Distinct()
                .OrderBy(id => id))
            {
                var product = await _consistency.LockProductAsync(
                    productId,
                    activeOnly: false,
                    cancellationToken)
                    ?? throw new BusinessException(
                        "Dữ liệu sản phẩm của giỏ hàng hoặc "
                        + "đơn hàng không còn tồn tại.");
                products.Add(productId, product);
            }
            return products;
        }

        private static string NormalizeIdempotencyKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BusinessException(
                    "Trường Idempotency-Key trong tiêu đề yêu cầu "
                    + "là bắt buộc khi đặt hàng.");
            }
            var normalized = value.Trim();
            if (normalized.Length > 100)
            {
                throw new BusinessException(
                    "Trường Idempotency-Key trong tiêu đề yêu cầu "
                    + "không được vượt quá 100 ký tự.");
            }
            return normalized;
        }

        private static string HashRequest(PlaceOrderRequest request)
        {
            var baseCanonical = string.Join(
                '\n',
                request.ShippingAddress.Trim(),
                NormalizeOptional(request.Note) ?? string.Empty,
                ((int)request.PaymentMethod).ToString());
            var normalizedPromotionCode =
                string.IsNullOrWhiteSpace(request.PromotionCode)
                    ? null
                    : DomainRuleGuard.AsBusiness(() =>
                        Promotion.NormalizeCode(
                            request.PromotionCode));
            var canonical =
                request.ShippingMethod == ShippingMethod.Standard
                && normalizedPromotionCode == null
                && !request.ExpectedTotalAmount.HasValue
                    ? baseCanonical
                    : string.Join(
                        '\n',
                        baseCanonical,
                        ((int)request.ShippingMethod).ToString(),
                        normalizedPromotionCode ?? string.Empty);
            if (request.ExpectedTotalAmount.HasValue)
            {
                canonical = string.Join(
                    '\n',
                    canonical,
                    request.ExpectedTotalAmount.Value.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture));
            }
            return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }

        private static void EnsureSameRequest(
            Order order,
            string requestHash)
        {
            if (!string.Equals(
                order.IdempotencyRequestHash,
                requestHash,
                StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "Idempotency-Key đã được sử dụng cho "
                    + "một yêu cầu đặt hàng khác.");
            }
        }

        private static string CreateOrderNumber(DateTime occurredAt)
            => $"ORD-{occurredAt:yyyyMMdd}-{Guid.NewGuid():N}"
                [..32]
                .ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
