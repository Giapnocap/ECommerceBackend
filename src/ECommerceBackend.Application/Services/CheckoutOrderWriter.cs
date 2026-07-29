using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Application.Services
{
    public sealed class CheckoutOrderWriter
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly ICartRepository _cartRepository;
        private readonly IInventoryRepository _inventoryRepository;

        public CheckoutOrderWriter(
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            ICartRepository cartRepository,
            IInventoryRepository inventoryRepository)
        {
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _cartRepository = cartRepository;
            _inventoryRepository = inventoryRepository;
        }

        internal void AddRecords(
            Order order,
            Payment payment,
            Cart cart,
            Guid userId,
            DateTime occurredAt)
        {
            _orderRepository.Add(order);
            _paymentRepository.Add(payment);
            _paymentRepository.AddStatusHistory(
                new PaymentStatusHistory
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
            _orderRepository.AddStatusHistory(
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ChangedByUserId = userId,
                    FromStatus = null,
                    ToStatus = order.Status,
                    Note = "Đã đặt hàng và giữ tồn kho",
                    CreatedAt = occurredAt
                });

            foreach (var item in cart.CartItems.ToList())
            {
                var product = item.Product!;
                var mutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.Reserve(product, item.Quantity));
                _orderRepository.AddDetail(
                    new OrderDetail
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
                DomainRuleGuard.AsBusiness(() =>
                    cart.RemoveItem(item));
                _cartRepository.RemoveItem(item);
            }
        }
    }
}
