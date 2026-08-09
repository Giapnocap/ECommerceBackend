using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class Payment
    {
        internal Payment()
        {
        }

        public Guid Id { get; internal set; }
        public Guid OrderId { get; internal set; }
        public PaymentMethod Method { get; internal set; }
        public PaymentStatus Status { get; private set; } = PaymentStatusTransitions.Initial;
        public decimal Amount { get; internal set; }
        public string? Provider { get; internal set; }
        public string? ProviderTransactionId { get; internal set; }
        public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public Order? Order { get; set; }
        public ICollection<PaymentWebhookEvent> WebhookEvents { get; set; } = new List<PaymentWebhookEvent>();
        public ICollection<PaymentStatusHistory> StatusHistory { get; set; } = new List<PaymentStatusHistory>();

        public static Payment Create(
            Guid id,
            Guid orderId,
            PaymentMethod method,
            decimal amount,
            string? provider,
            string? providerTransactionId,
            DateTime createdAt)
        {
            if (id == Guid.Empty || orderId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "payment_identity_invalid",
                    "Thông tin định danh của giao dịch thanh toán không hợp lệ.");
            }

            if (!Enum.IsDefined(method) || amount <= 0)
            {
                throw new DomainRuleViolationException(
                    "payment_details_invalid",
                    "Phương thức hoặc số tiền thanh toán không hợp lệ.");
            }

            if (provider?.Trim().Length > 100
                || providerTransactionId?.Trim().Length > 200)
            {
                throw new DomainRuleViolationException(
                    "payment_provider_reference_invalid",
                    "Thông tin tham chiếu của cổng thanh toán không hợp lệ.");
            }

            return new Payment
            {
                Id = id,
                OrderId = orderId,
                Method = method,
                Amount = amount,
                Provider = string.IsNullOrWhiteSpace(provider)
                    ? null
                    : provider.Trim(),
                ProviderTransactionId =
                    string.IsNullOrWhiteSpace(providerTransactionId)
                        ? null
                        : providerTransactionId.Trim(),
                CreatedAt = createdAt
            };
        }

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
