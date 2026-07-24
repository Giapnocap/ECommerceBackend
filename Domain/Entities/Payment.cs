using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class Payment
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public PaymentMethod Method { get; set; }
        public PaymentStatus Status { get; private set; } = PaymentStatusTransitions.Initial;
        public decimal Amount { get; set; }
        public string? Provider { get; set; }
        public string? ProviderTransactionId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public Order? Order { get; set; }
        public ICollection<PaymentWebhookEvent> WebhookEvents { get; set; } = new List<PaymentWebhookEvent>();
        public ICollection<PaymentStatusHistory> StatusHistory { get; set; } = new List<PaymentStatusHistory>();

        public StatusChange<PaymentStatus> ChangeStatus(
            PaymentStatus nextStatus,
            DateTime occurredAt)
        {
            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "payment_occurrence_before_creation",
                    "Thời điểm thay đổi trạng thái thanh toán không được trước thời điểm tạo giao dịch.");
            }

            if (nextStatus == PaymentStatus.Refunded
                && PaidAt.HasValue
                && occurredAt < PaidAt.Value)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_before_paid",
                    "Không thể hoàn tiền trước thời điểm giao dịch được thanh toán.");
            }

            var previousStatus = Status;
            if (previousStatus == nextStatus)
                return new StatusChange<PaymentStatus>(previousStatus, nextStatus, false);

            if (!previousStatus.CanTransitionTo(nextStatus))
            {
                throw new DomainRuleViolationException(
                    "payment_status_transition_invalid",
                    $"Không thể chuyển giao dịch thanh toán từ trạng thái '{GetStatusLabel(previousStatus)}' sang '{GetStatusLabel(nextStatus)}'.");
            }

            if (nextStatus == PaymentStatus.Refunded && !PaidAt.HasValue)
            {
                throw new DomainRuleViolationException(
                    "payment_paid_at_missing",
                    "Không thể hoàn tiền cho giao dịch chưa ghi nhận thời điểm thanh toán.");
            }

            Status = nextStatus;
            if (nextStatus == PaymentStatus.Paid)
                PaidAt = occurredAt;
            else if (nextStatus is PaymentStatus.Failed or PaymentStatus.Cancelled)
                PaidAt = null;

            return new StatusChange<PaymentStatus>(previousStatus, nextStatus, true);
        }

        private static string GetStatusLabel(PaymentStatus status)
            => status switch
            {
                PaymentStatus.Pending => "Chờ thanh toán",
                PaymentStatus.Paid => "Đã thanh toán",
                PaymentStatus.Failed => "Thất bại",
                PaymentStatus.Cancelled => "Đã hủy",
                PaymentStatus.Refunded => "Đã hoàn tiền",
                _ => status.ToString()
            };
    }
}
