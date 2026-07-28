using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class ReturnRequest
    {
        public Guid Id { get; private set; }
        public Guid OrderId { get; private set; }
        public Guid RequestedByUserId { get; private set; }
        public string Reason { get; private set; } = string.Empty;
        public ReturnRequestStatus Status { get; private set; }
        public DateTime RequestedAt { get; private set; }
        public Guid? ReviewedByUserId { get; private set; }
        public DateTime? ReviewedAt { get; private set; }
        public string? ReviewNote { get; private set; }
        public Guid? ReceivedByUserId { get; private set; }
        public DateTime? ReceivedAt { get; private set; }
        public string? InspectionNote { get; private set; }
        public DateTime? RefundedAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public Order? Order { get; set; }
        public User? RequestedByUser { get; set; }
        public User? ReviewedByUser { get; set; }
        public User? ReceivedByUser { get; set; }

        public static ReturnRequest Create(
            Guid id,
            Guid orderId,
            Guid requestedByUserId,
            string reason,
            DateTime requestedAt)
        {
            if (id == Guid.Empty || orderId == Guid.Empty
                || requestedByUserId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "return_request_identity_invalid",
                    "Thông tin định danh của yêu cầu trả hàng không hợp lệ.");
            }

            return new ReturnRequest
            {
                Id = id,
                OrderId = orderId,
                RequestedByUserId = requestedByUserId,
                Reason = NormalizeRequired(
                    reason,
                    500,
                    "return_reason_invalid",
                    "Lý do trả hàng"),
                Status = ReturnRequestStatus.Pending,
                RequestedAt = requestedAt
            };
        }

        public void Review(
            ReturnReviewDecision decision,
            Guid reviewedByUserId,
            DateTime reviewedAt,
            string? note)
        {
            if (Status != ReturnRequestStatus.Pending)
            {
                throw new DomainRuleViolationException(
                    "return_request_already_reviewed",
                    "Yêu cầu trả hàng đã được xử lý.");
            }

            if (!Enum.IsDefined(decision))
            {
                throw new DomainRuleViolationException(
                    "return_review_decision_invalid",
                    "Quyết định xét duyệt trả hàng không hợp lệ.");
            }

            if (reviewedByUserId == Guid.Empty || reviewedAt < RequestedAt)
            {
                throw new DomainRuleViolationException(
                    "return_review_invalid",
                    "Thông tin xét duyệt trả hàng không hợp lệ.");
            }

            var normalizedNote = NormalizeOptional(note, 500, "return_review_note_invalid");
            if (decision == ReturnReviewDecision.Reject
                && normalizedNote == null)
            {
                throw new DomainRuleViolationException(
                    "return_rejection_note_required",
                    "Phải nhập lý do từ chối yêu cầu trả hàng.");
            }

            Status = decision == ReturnReviewDecision.Approve
                ? ReturnRequestStatus.Approved
                : ReturnRequestStatus.Rejected;
            ReviewedByUserId = reviewedByUserId;
            ReviewedAt = reviewedAt;
            ReviewNote = normalizedNote;
        }

        public void Receive(
            Guid receivedByUserId,
            DateTime receivedAt,
            string inspectionNote)
        {
            if (Status != ReturnRequestStatus.Approved)
            {
                throw new DomainRuleViolationException(
                    "return_receive_requires_approved",
                    "Chỉ có thể nhận hàng hoàn của yêu cầu đã được duyệt.");
            }

            if (receivedByUserId == Guid.Empty
                || !ReviewedAt.HasValue
                || receivedAt < ReviewedAt.Value)
            {
                throw new DomainRuleViolationException(
                    "return_receive_invalid",
                    "Thông tin nhận hàng hoàn không hợp lệ.");
            }

            Status = ReturnRequestStatus.Received;
            ReceivedByUserId = receivedByUserId;
            ReceivedAt = receivedAt;
            InspectionNote = NormalizeRequired(
                inspectionNote,
                500,
                "return_inspection_note_invalid",
                "Kết quả kiểm tra hàng hoàn");
        }

        public void MarkRefunded(DateTime refundedAt)
        {
            if (Status != ReturnRequestStatus.Received)
            {
                throw new DomainRuleViolationException(
                    "return_refund_requires_received",
                    "Chỉ có thể hoàn tiền sau khi đã nhận và kiểm tra hàng hoàn.");
            }

            if (!ReceivedAt.HasValue || refundedAt < ReceivedAt.Value)
            {
                throw new DomainRuleViolationException(
                    "return_refund_time_invalid",
                    "Thời điểm hoàn tiền không hợp lệ.");
            }

            Status = ReturnRequestStatus.Refunded;
            RefundedAt = refundedAt;
        }

        private static string NormalizeRequired(
            string value,
            int maximumLength,
            string code,
            string fieldName)
            => NormalizeOptional(value, maximumLength, code)
                ?? throw new DomainRuleViolationException(
                    code,
                    $"{fieldName} không được để trống.");

        private static string? NormalizeOptional(
            string? value,
            int maximumLength,
            string code)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim();
            if (normalized.Length > maximumLength)
            {
                throw new DomainRuleViolationException(
                    code,
                    $"Nội dung không được vượt quá {maximumLength} ký tự.");
            }

            return normalized;
        }
    }
}
