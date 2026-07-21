using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class Order
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
        public string IdempotencyRequestHash { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; } = DateTime.UtcNow;
        public decimal SubtotalAmount { get; private set; }
        public decimal DiscountAmount { get; private set; }
        public decimal ShippingFee { get; private set; }
        public decimal TaxAmount { get; private set; }
        public decimal TotalAmount { get; private set; }
        public OrderStatus Status { get; private set; } = OrderStatusTransitions.Initial;
        public string ShippingAddress { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime? ExpiresAt { get; private set; }
        public DateTime? CancelledAt { get; private set; }
        public DateTime? ExpiredAt { get; private set; }
        public string? CancellationReason { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public User? User { get; set; }
        public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
        public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();
        public Payment? Payment { get; set; }

        public void SetPricing(
            decimal subtotal,
            decimal discount,
            decimal shipping,
            decimal tax)
        {
            var amounts = OrderPricingPolicy.CalculateAmounts(
                subtotal,
                discount,
                shipping,
                tax);

            SubtotalAmount = amounts.Subtotal;
            DiscountAmount = amounts.Discount;
            ShippingFee = amounts.Shipping;
            TaxAmount = amounts.Tax;
            TotalAmount = amounts.Total;
        }

        public StatusChange<OrderStatus> ChangeStatus(
            OrderStatus nextStatus,
            PaymentStatus? paymentStatus)
        {
            var previousStatus = Status;
            if (previousStatus == nextStatus)
                return new StatusChange<OrderStatus>(previousStatus, nextStatus, false);

            if (!previousStatus.CanTransitionTo(nextStatus))
            {
                throw new DomainRuleViolationException(
                    "order_status_transition_invalid",
                    $"Cannot transition order from '{previousStatus}' to '{nextStatus}'.");
            }

            if (nextStatus == OrderStatus.Cancelled
                && paymentStatus == PaymentStatus.Paid)
            {
                throw new DomainRuleViolationException(
                    "order_paid_cancellation_forbidden",
                    "A paid order cannot be cancelled before its payment is refunded.");
            }

            Status = nextStatus;
            return new StatusChange<OrderStatus>(previousStatus, nextStatus, true);
        }

        public void SetPendingExpiration(DateTime expiresAt)
        {
            if (Status != OrderStatus.Pending)
            {
                throw new DomainRuleViolationException(
                    "order_expiration_requires_pending",
                    "Only a pending order can receive an expiration time.");
            }

            if (expiresAt <= OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_expiration_invalid",
                    "Order expiration must be later than the order date.");
            }

            ExpiresAt = expiresAt;
        }

        public StatusChange<OrderStatus> Cancel(
            DateTime occurredAt,
            PaymentStatus? paymentStatus,
            string reason,
            bool isExpiration = false)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200)
            {
                throw new DomainRuleViolationException(
                    "order_cancellation_reason_invalid",
                    "Cancellation reason must contain between 1 and 200 characters.");
            }

            if (occurredAt < OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_cancellation_time_invalid",
                    "Cancellation time cannot be earlier than the order date.");
            }

            if (isExpiration && (!ExpiresAt.HasValue || occurredAt < ExpiresAt.Value))
            {
                throw new DomainRuleViolationException(
                    "order_not_expired",
                    "The order has not reached its expiration time.");
            }

            var statusChange = ChangeStatus(OrderStatus.Cancelled, paymentStatus);
            if (!statusChange.Changed)
                return statusChange;

            CancelledAt = occurredAt;
            CancellationReason = reason.Trim();
            ExpiredAt = isExpiration ? occurredAt : null;
            return statusChange;
        }
    }
}
