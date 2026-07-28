using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderCommandService
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;
        private readonly OrderQueryUseCase _queries;

        public OrderCommandService(
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            IInventoryRepository inventoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderQueryUseCase queries,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _inventoryRepository = inventoryRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _queries = queries;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<OrderResponse> UpdateStatusAsync(
            Guid orderId,
            Guid actorUserId,
            UpdateOrderStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureGenericTransitionIsAllowed(request.Status);
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
                        await RestoreOrderStockAsync(
                            order,
                            actorUserId,
                            occurredAt,
                            InventoryTransactionType.OrderCancelled,
                            cancellationToken);
                    }

                    UpdatePaymentForOrderStatus(
                        payment,
                        request.Status,
                        actorUserId,
                        occurredAt);

                    _orderRepository.AddStatusHistory(new OrderStatusHistory
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
                        $"Đơn hàng {order.OrderNumber} đã chuyển sang trạng thái {GetOrderStatusLabel(request.Status)}.",
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

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

            return await _queries.GetByIdAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);
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
                    return await _queries.GetByIdAsync(
                        orderId,
                        customerUserId,
                        false,
                        cancellationToken);
                }

                if (order.Status != OrderStatus.Pending)
                {
                    throw new ConflictException(
                        "customer_order_cancellation_forbidden",
                        "Khách hàng chỉ có thể hủy đơn hàng đang chờ xử lý.");
                }

                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken);
                var occurredAt = UtcNow;
                var statusChange = DomainRuleGuard.AsConflict(() => order.Cancel(
                    occurredAt,
                    payment?.Status,
                    NormalizeOptional(request.Reason) ?? "Khách hàng yêu cầu hủy"));

                await RestoreOrderStockAsync(
                    order,
                    customerUserId,
                    occurredAt,
                    InventoryTransactionType.OrderCancelled,
                    cancellationToken);
                UpdatePaymentForOrderStatus(
                    payment,
                    OrderStatus.Cancelled,
                    customerUserId,
                    occurredAt);
                AddCancellationHistory(
                    order,
                    statusChange,
                    customerUserId,
                    request.Reason,
                    occurredAt);
                EnqueueCancellationNotification(
                    order,
                    payment,
                    "Đơn hàng đã được hủy theo yêu cầu của bạn.");

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

            return await _queries.GetByIdAsync(
                orderId,
                customerUserId,
                false,
                cancellationToken);
        }

        public async Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            if (batchSize <= 0)
                return Array.Empty<Guid>();

            return await _orderRepository.GetDuePendingOrderIdsAsync(
                asOf,
                batchSize,
                cancellationToken);
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

                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken);
                var statusChange = DomainRuleGuard.AsConflict(() => order.Cancel(
                    asOf,
                    payment?.Status,
                    "SystemExpired",
                    isExpiration: true));

                await RestoreOrderStockAsync(
                    order,
                    null,
                    asOf,
                    InventoryTransactionType.OrderCancelled,
                    cancellationToken);
                UpdatePaymentForOrderStatus(
                    payment,
                    OrderStatus.Cancelled,
                    null,
                    asOf);
                AddCancellationHistory(
                    order,
                    statusChange,
                    null,
                    "SystemExpired",
                    asOf);
                EnqueueCancellationNotification(
                    order,
                    payment,
                    "Đơn hàng đã hết thời gian giữ tồn kho và được hủy tự động.");

                await _unitOfWork.SaveChangesAsync(cancellationToken);
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
                    ?? throw new BusinessException(
                        "Dữ liệu sản phẩm của đơn hàng không còn tồn tại.");

                products.Add(productId, product);
            }

            return products;
        }

        private async Task RestoreOrderStockAsync(
            Order order,
            Guid? actorUserId,
            DateTime occurredAt,
            InventoryTransactionType transactionType,
            CancellationToken cancellationToken)
        {
            await _orderRepository.LoadDetailsAsync(
                order,
                cancellationToken);
            var products = await LoadProductsForUpdateAsync(
                order.OrderDetails.Select(detail => detail.ProductId),
                cancellationToken);

            foreach (var detail in order.OrderDetails)
            {
                var product = products[detail.ProductId];
                var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.Release(product, detail.Quantity));

                _inventoryRepository.AddTransaction(new InventoryTransaction
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    OrderId = order.Id,
                    CreatedByUserId = actorUserId,
                    Type = transactionType,
                    QuantityChange = inventoryMutation.QuantityChange,
                    BalanceAfter = inventoryMutation.BalanceAfter,
                    Reason = $"Hoàn kho do hủy đơn {order.OrderNumber}",
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

            if (orderStatus != OrderStatus.Cancelled
                || payment.Status != PaymentStatus.Pending)
            {
                return;
            }

            var statusChange = DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(PaymentStatus.Cancelled, occurredAt));

            _paymentRepository.AddStatusHistory(new PaymentStatusHistory
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                ChangedByUserId = actorUserId,
                FromStatus = statusChange.Previous,
                ToStatus = PaymentStatus.Cancelled,
                Source = PaymentStatusChangeSource.OrderLifecycle,
                Reference = GetOrderStatusLabel(orderStatus),
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
            _orderRepository.AddStatusHistory(new OrderStatusHistory
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

        private static void EnsureGenericTransitionIsAllowed(
            OrderStatus requestedStatus)
        {
            if (requestedStatus is OrderStatus.Shipping
                or OrderStatus.Delivered
                or OrderStatus.ReturnRequested
                or OrderStatus.ReturnApproved
                or OrderStatus.Returned
                or OrderStatus.Refunded)
            {
                throw new ConflictException(
                    "order_managed_transition_required",
                    "Trạng thái giao hàng, trả hàng và hoàn tiền phải được cập nhật qua API nghiệp vụ tương ứng.");
            }
        }

        private static string GetOrderStatusLabel(OrderStatus status)
            => status switch
            {
                OrderStatus.Pending => "Chờ xác nhận",
                OrderStatus.Confirmed => "Đã xác nhận",
                OrderStatus.Shipping => "Đang giao",
                OrderStatus.Delivered => "Đã giao",
                OrderStatus.Cancelled => "Đã hủy",
                OrderStatus.DeliveryFailed => "Giao thất bại",
                OrderStatus.Returned => "Đã nhận hàng hoàn",
                OrderStatus.ReturnRequested => "Đã yêu cầu trả hàng",
                OrderStatus.ReturnApproved => "Đã duyệt trả hàng",
                OrderStatus.Refunded => "Đã hoàn tiền",
                _ => status.ToString()
            };

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
