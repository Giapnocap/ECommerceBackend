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
        public Guid? PromotionId { get; set; }
        public string? PromotionCodeSnapshot { get; set; }
        public ShippingMethod ShippingMethod { get; set; }
        public string Currency { get; set; } = "VND";
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
        public Promotion? Promotion { get; set; }
        public PromotionRedemption? PromotionRedemption { get; set; }

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
                    $"Không thể chuyển đơn hàng từ trạng thái '{GetStatusLabel(previousStatus)}' sang '{GetStatusLabel(nextStatus)}'.");
            }

            if (nextStatus == OrderStatus.Cancelled
                && paymentStatus == PaymentStatus.Paid)
            {
                throw new DomainRuleViolationException(
                    "order_paid_cancellation_forbidden",
                    "Không thể hủy đơn hàng đã thanh toán trước khi hoàn tiền.");
            }

            if (nextStatus == OrderStatus.Returned
                && paymentStatus is not (PaymentStatus.Paid or PaymentStatus.Refunded))
            {
                throw new DomainRuleViolationException(
                    "order_return_requires_collected_payment",
                    "Chỉ có thể ghi nhận hoàn hàng sau khi đơn hàng đã thu tiền.");
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
                    "Chỉ đơn hàng đang chờ xác nhận mới có thể đặt thời hạn xử lý.");
            }

            if (expiresAt <= OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_expiration_invalid",
                    "Thời hạn xử lý đơn hàng phải sau thời điểm đặt hàng.");
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
                    "Lý do hủy phải có từ 1 đến 200 ký tự.");
            }

            if (occurredAt < OrderDate)
            {
                throw new DomainRuleViolationException(
                    "order_cancellation_time_invalid",
                    "Thời điểm hủy không được trước thời điểm đặt hàng.");
            }

            if (isExpiration && (!ExpiresAt.HasValue || occurredAt < ExpiresAt.Value))
            {
                throw new DomainRuleViolationException(
                    "order_not_expired",
                    "Đơn hàng chưa đến thời điểm hết hạn.");
            }

            var statusChange = ChangeStatus(OrderStatus.Cancelled, paymentStatus);
            if (!statusChange.Changed)
                return statusChange;

            CancelledAt = occurredAt;
            CancellationReason = reason.Trim();
            ExpiredAt = isExpiration ? occurredAt : null;
            return statusChange;
        }

        private static string GetStatusLabel(OrderStatus status)
            => status switch
            {
                OrderStatus.Pending => "Chờ xác nhận",
                OrderStatus.Confirmed => "Đã xác nhận",
                OrderStatus.Shipping => "Đang giao",
                OrderStatus.Delivered => "Đã giao",
                OrderStatus.Cancelled => "Đã hủy",
                OrderStatus.DeliveryFailed => "Giao thất bại",
                OrderStatus.Returned => "Đã hoàn hàng",
                _ => status.ToString()
            };
    }
}
