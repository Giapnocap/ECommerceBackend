using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class PaymentRefund
    {
        internal PaymentRefund()
        {
        }

        public Guid Id { get; internal set; }
        public Guid PaymentId { get; internal set; }
        public Guid RequestedByUserId { get; internal set; }
        public string IdempotencyKey { get; internal set; } = string.Empty;
        public decimal Amount { get; internal set; }
        public string Currency { get; internal set; } = "VND";
        public decimal BaseAmount { get; internal set; }
        public string BaseCurrency { get; internal set; } = CurrencyCatalog.BaseCurrency;
        public PaymentRefundStatus Status { get; private set; }
        public string? ProviderRefundId { get; private set; }
        public int AttemptCount { get; private set; }
        public DateTime RequestedAt { get; internal set; }
        public DateTime? ProcessingLeaseUntil { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public string? FailureCode { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public Payment? Payment { get; set; }

        public static PaymentRefund Create(
            Guid id,
            Guid paymentId,
            Guid requestedByUserId,
            string idempotencyKey,
            decimal amount,
            string currency,
            decimal baseAmount,
            string baseCurrency,
            DateTime requestedAt)
        {
            var normalizedCurrency = currency?.Trim().ToUpperInvariant();
            var normalizedBaseCurrency = baseCurrency?.Trim().ToUpperInvariant();
            if (id == Guid.Empty
                || paymentId == Guid.Empty
                || requestedByUserId == Guid.Empty
                || string.IsNullOrWhiteSpace(idempotencyKey)
                || idempotencyKey.Trim().Length > 200
                || amount <= 0
                || baseAmount <= 0
                || normalizedCurrency?.Length != 3
                || normalizedBaseCurrency?.Length != 3
                || normalizedCurrency.Any(character =>
                    character is < 'A' or > 'Z')
                || normalizedBaseCurrency.Any(character =>
                    character is < 'A' or > 'Z'))
            {
                throw new DomainRuleViolationException(
                    "payment_refund_invalid",
                    "Thông tin yêu cầu hoàn tiền không hợp lệ.");
            }

            _ = new Money(amount, normalizedCurrency);
            _ = new Money(baseAmount, normalizedBaseCurrency);

            return new PaymentRefund
            {
                Id = id,
                PaymentId = paymentId,
                RequestedByUserId = requestedByUserId,
                IdempotencyKey = idempotencyKey.Trim(),
                Amount = amount,
                Currency = normalizedCurrency,
                BaseAmount = baseAmount,
                BaseCurrency = normalizedBaseCurrency,
                Status = PaymentRefundStatus.Pending,
                RequestedAt = requestedAt
            };
        }

        public bool StartProcessing(
            DateTime occurredAt,
            DateTime leaseUntil)
        {
            if (Status == PaymentRefundStatus.Succeeded)
                return false;

            if (Status == PaymentRefundStatus.Processing
                && ProcessingLeaseUntil > occurredAt)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_in_progress",
                    "Yêu cầu hoàn tiền đang được xử lý.");
            }

            if (leaseUntil <= occurredAt)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_lease_invalid",
                    "Thời hạn xử lý hoàn tiền không hợp lệ.");
            }

            Status = PaymentRefundStatus.Processing;
            ProcessingLeaseUntil = leaseUntil;
            FailureCode = null;
            AttemptCount++;
            return true;
        }

        public void MarkPending()
        {
            if (Status != PaymentRefundStatus.Succeeded)
            {
                Status = PaymentRefundStatus.Pending;
                ProcessingLeaseUntil = null;
            }
        }

        public void Complete(
            string providerRefundId,
            DateTime occurredAt)
        {
            var normalizedReference = providerRefundId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedReference)
                || normalizedReference.Length > 200
                || occurredAt < RequestedAt)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_completion_invalid",
                    "Kết quả hoàn tiền từ cổng thanh toán không hợp lệ.");
            }

            if (Status == PaymentRefundStatus.Succeeded)
            {
                if (!string.Equals(
                    ProviderRefundId,
                    normalizedReference,
                    StringComparison.Ordinal))
                {
                    throw new DomainRuleViolationException(
                        "payment_refund_reference_conflict",
                        "Yêu cầu hoàn tiền đã có mã cổng thanh toán khác.");
                }
                return;
            }

            ProviderRefundId = normalizedReference;
            Status = PaymentRefundStatus.Succeeded;
            CompletedAt = occurredAt;
            ProcessingLeaseUntil = null;
            FailureCode = null;
        }

        public void Fail(string failureCode)
        {
            if (Status == PaymentRefundStatus.Succeeded)
                return;

            var normalizedCode = failureCode?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedCode)
                || normalizedCode.Length > 100)
            {
                throw new DomainRuleViolationException(
                    "payment_refund_failure_invalid",
                    "Mã lỗi hoàn tiền không hợp lệ.");
            }

            Status = PaymentRefundStatus.Failed;
            FailureCode = normalizedCode;
            ProcessingLeaseUntil = null;
        }
    }
}
