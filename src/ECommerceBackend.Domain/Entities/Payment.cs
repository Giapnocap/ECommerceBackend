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
        public string Currency { get; internal set; } = "VND";
        public decimal RefundedAmount { get; private set; }
        public string? Provider { get; internal set; }
        public string? ProviderTransactionId { get; internal set; }
        public string? ExternalCreationIdempotencyKey { get; private set; }
        public DateTime? ExternalCreationLeaseUntil { get; private set; }
        public DateTime? ExternalCreatedAt { get; private set; }
        public DateTime? LastProviderEventAt { get; private set; }
        public DateTime? LastReconciledAt { get; private set; }
        public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
        public DateTime? PaidAt { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public Order? Order { get; set; }
        public ICollection<PaymentWebhookEvent> WebhookEvents { get; set; } = new List<PaymentWebhookEvent>();
        public ICollection<PaymentStatusHistory> StatusHistory { get; set; } = new List<PaymentStatusHistory>();
        public ICollection<PaymentRefund> Refunds { get; set; } = new List<PaymentRefund>();

        public static Payment Create(
            Guid id,
            Guid orderId,
            PaymentMethod method,
            decimal amount,
            string? provider,
            string? providerTransactionId,
            DateTime createdAt,
            string currency = "VND")
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

            var normalizedCurrency = currency?.Trim().ToUpperInvariant();
            if (normalizedCurrency?.Length != 3
                || normalizedCurrency.Any(character =>
                    character is < 'A' or > 'Z'))
            {
                throw new DomainRuleViolationException(
                    "payment_currency_invalid",
                    "Mã tiền tệ thanh toán phải gồm 3 chữ cái theo chuẩn ISO.");
            }

            return new Payment
            {
                Id = id,
                OrderId = orderId,
                Method = method,
                Amount = amount,
                Currency = normalizedCurrency,
                Provider = string.IsNullOrWhiteSpace(provider)
                    ? null
                    : provider.Trim(),
                ProviderTransactionId =
                    string.IsNullOrWhiteSpace(providerTransactionId)
                        ? null
                        : providerTransactionId.Trim(),
                ExternalCreationIdempotencyKey = method == PaymentMethod.Card
                    ? $"payment-{id:N}"
                    : null,
                ExternalCreatedAt = string.IsNullOrWhiteSpace(
                    providerTransactionId)
                        ? null
                        : createdAt,
                CreatedAt = createdAt
            };
        }

        public bool ClaimExternalCreation(
            DateTime occurredAt,
            DateTime leaseUntil)
        {
            if (Method != PaymentMethod.Card
                || string.IsNullOrWhiteSpace(ExternalCreationIdempotencyKey))
            {
                throw new DomainRuleViolationException(
                    "payment_external_creation_unsupported",
                    "Phương thức thanh toán này không cần khởi tạo tại cổng thanh toán.");
            }

            if (ProviderTransactionId != null)
                return false;

            if (Status is PaymentStatus.Failed
                or PaymentStatus.Cancelled
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded)
            {
                throw new DomainRuleViolationException(
                    "payment_external_creation_state_invalid",
                    "Trạng thái thanh toán hiện tại không cho phép khởi tạo giao dịch ngoài.");
            }

            if (ExternalCreationLeaseUntil > occurredAt)
            {
                throw new DomainRuleViolationException(
                    "payment_external_creation_in_progress",
                    "Giao dịch thanh toán đang được khởi tạo bởi một yêu cầu khác.");
            }

            if (leaseUntil <= occurredAt)
            {
                throw new DomainRuleViolationException(
                    "payment_external_creation_lease_invalid",
                    "Thời hạn xử lý giao dịch ngoài không hợp lệ.");
            }

            ExternalCreationLeaseUntil = leaseUntil;
            return true;
        }

        public StatusChange<PaymentStatus> AttachProviderTransaction(
            string provider,
            string providerTransactionId,
            PaymentStatus status,
            DateTime occurredAt)
        {
            var normalizedProvider = provider?.Trim().ToLowerInvariant();
            var normalizedTransactionId = providerTransactionId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedProvider)
                || normalizedProvider.Length > 100
                || string.IsNullOrWhiteSpace(normalizedTransactionId)
                || normalizedTransactionId.Length > 200)
            {
                throw new DomainRuleViolationException(
                    "payment_provider_reference_invalid",
                    "Thông tin tham chiếu của cổng thanh toán không hợp lệ.");
            }

            if (!string.Equals(
                    Provider,
                    normalizedProvider,
                    StringComparison.OrdinalIgnoreCase)
                || ProviderTransactionId != null
                    && !string.Equals(
                        ProviderTransactionId,
                        normalizedTransactionId,
                        StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    "payment_provider_reference_conflict",
                    "Giao dịch đã được gắn với một tham chiếu cổng thanh toán khác.");
            }

            Provider = normalizedProvider;
            ProviderTransactionId = normalizedTransactionId;
            ExternalCreatedAt ??= occurredAt;
            ExternalCreationLeaseUntil = null;
            return ChangeStatus(status, occurredAt);
        }

        public void ReleaseExternalCreationClaim()
        {
            if (ProviderTransactionId == null)
                ExternalCreationLeaseUntil = null;
        }

        public bool IsProviderEventStale(DateTime occurredAt)
            => LastProviderEventAt.HasValue
                && occurredAt < LastProviderEventAt.Value;

        public bool HasActiveExternalTransaction
            => Method == PaymentMethod.Card
                && ProviderTransactionId != null
                && Status is PaymentStatus.Pending
                    or PaymentStatus.RequiresAction
                    or PaymentStatus.Processing;

        public void MarkProviderEventApplied(DateTime occurredAt)
        {
            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "payment_provider_event_time_invalid",
                    "Thời điểm sự kiện từ cổng thanh toán không hợp lệ.");
            }

            if (!IsProviderEventStale(occurredAt))
                LastProviderEventAt = occurredAt;
        }

        public StatusChange<PaymentStatus> ReconcileProviderStatus(
            PaymentStatus status,
            DateTime observedAt)
        {
            var change = ChangeStatus(status, observedAt);
            if (!LastReconciledAt.HasValue
                || observedAt > LastReconciledAt.Value)
            {
                LastReconciledAt = observedAt;
            }

            return change;
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

            if (nextStatus is PaymentStatus.Refunded
                    or PaymentStatus.PartiallyRefunded
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

            if (nextStatus is PaymentStatus.Refunded
                    or PaymentStatus.PartiallyRefunded
                && !PaidAt.HasValue)
            {
                throw new DomainRuleViolationException(
                    "payment_paid_at_missing",
                    "Không thể hoàn tiền cho giao dịch chưa ghi nhận thời điểm thanh toán.");
            }

            if (nextStatus == PaymentStatus.PartiallyRefunded)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_amount_required",
                    "Hoàn tiền một phần phải chỉ rõ số tiền hoàn.");
            }

            Status = nextStatus;
            if (nextStatus == PaymentStatus.Paid)
                PaidAt = occurredAt;
            else if (nextStatus == PaymentStatus.Refunded)
                RefundedAmount = Amount;
            else if (nextStatus is PaymentStatus.Failed or PaymentStatus.Cancelled)
                PaidAt = null;

            return new StatusChange<PaymentStatus>(previousStatus, nextStatus, true);
        }

        public StatusChange<PaymentStatus> RecordRefund(
            decimal amount,
            DateTime occurredAt)
        {
            if (Status is not (PaymentStatus.Paid
                or PaymentStatus.PartiallyRefunded))
            {
                throw new DomainRuleViolationException(
                    "payment_not_refundable",
                    "Chỉ có thể hoàn tiền cho giao dịch đã thanh toán.");
            }

            if (!PaidAt.HasValue || occurredAt < PaidAt.Value)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_time_invalid",
                    "Thời điểm hoàn tiền không hợp lệ.");
            }

            if (amount <= 0 || RefundedAmount + amount > Amount)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_amount_invalid",
                    "Tổng số tiền hoàn phải lớn hơn 0 và không vượt quá số tiền đã thanh toán.");
            }

            var previousStatus = Status;
            RefundedAmount += amount;
            Status = RefundedAmount == Amount
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            return new StatusChange<PaymentStatus>(
                previousStatus,
                Status,
                previousStatus != Status);
        }

        private static string GetStatusLabel(PaymentStatus status)
            => status switch
            {
                PaymentStatus.Pending => "Chờ thanh toán",
                PaymentStatus.Paid => "Đã thanh toán",
                PaymentStatus.Failed => "Thất bại",
                PaymentStatus.Cancelled => "Đã hủy",
                PaymentStatus.Refunded => "Đã hoàn tiền",
                PaymentStatus.RequiresAction => "Cần xác thực",
                PaymentStatus.Processing => "Đang xử lý",
                PaymentStatus.PartiallyRefunded => "Đã hoàn tiền một phần",
                _ => status.ToString()
            };
    }
}
