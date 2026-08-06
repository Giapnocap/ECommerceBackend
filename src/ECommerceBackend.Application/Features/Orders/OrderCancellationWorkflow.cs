using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderCancellationWorkflow
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;

        public OrderCancellationWorkflow(
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            IInventoryRepository inventoryRepository,
            IDataConsistencyService consistency,
            IOutboxWriter outbox)
        {
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _inventoryRepository = inventoryRepository;
            _consistency = consistency;
            _outbox = outbox;
        }

        internal async Task RestoreStockAsync(
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

                _inventoryRepository.AddTransaction(
                    new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        OrderId = order.Id,
                        CreatedByUserId = actorUserId,
                        Type = transactionType,
                        QuantityChange = inventoryMutation.QuantityChange,
                        BalanceAfter = inventoryMutation.BalanceAfter,
                        Reason =
                            $"Hoàn kho do hủy đơn {order.OrderNumber}",
                        CreatedAt = occurredAt
                    });
            }
        }

        internal void UpdatePayment(
            Payment? payment,
            OrderStatus orderStatus,
            Guid? actorUserId,
            DateTime occurredAt)
        {
            if (payment == null
                || orderStatus != OrderStatus.Cancelled
                || payment.Status != PaymentStatus.Pending)
            {
                return;
            }

            var statusChange = DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(PaymentStatus.Cancelled, occurredAt));

            _paymentRepository.AddStatusHistory(
                new PaymentStatusHistory
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    ChangedByUserId = actorUserId,
                    FromStatus = statusChange.Previous,
                    ToStatus = PaymentStatus.Cancelled,
                    Source = PaymentStatusChangeSource.OrderLifecycle,
                    Reference =
                        OrderCommandRules.GetStatusLabel(orderStatus),
                    OccurredAt = occurredAt,
                    CreatedAt = occurredAt
                });
        }

        internal void AddHistory(
            Order order,
            StatusChange<OrderStatus> statusChange,
            Guid? actorUserId,
            string? note,
            DateTime occurredAt)
        {
            _orderRepository.AddStatusHistory(
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ChangedByUserId = actorUserId,
                    FromStatus = statusChange.Previous,
                    ToStatus = statusChange.Current,
                    Note = OrderCommandRules.NormalizeOptional(note),
                    CreatedAt = occurredAt
                });
        }

        internal void EnqueueNotification(
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

        private async Task<Dictionary<Guid, Product>>
            LoadProductsForUpdateAsync(
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
    }
}
