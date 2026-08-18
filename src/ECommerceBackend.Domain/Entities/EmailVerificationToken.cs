using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class EmailVerificationToken
    {
        internal EmailVerificationToken()
        {
        }

        public Guid Id { get; internal set; }
        public Guid UserId { get; internal set; }
        public string TokenHash { get; internal set; } = string.Empty;
        public DateTime CreatedAt { get; internal set; }
        public DateTime ExpiresAt { get; internal set; }
        public DateTime? ConsumedAt { get; private set; }
        public DateTime? RevokedAt { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public User? User { get; set; }

        public static EmailVerificationToken Create(
            Guid id,
            Guid userId,
            string tokenHash,
            DateTime createdAt,
            DateTime expiresAt)
        {
            if (id == Guid.Empty || userId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "email_verification_token_identity_invalid",
                    "Thông tin định danh của mã xác minh email không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(tokenHash)
                || tokenHash.Length != 64
                || expiresAt <= createdAt)
            {
                throw new DomainRuleViolationException(
                    "email_verification_token_details_invalid",
                    "Giá trị băm hoặc thời hạn của mã xác minh email không hợp lệ.");
            }

            return new EmailVerificationToken
            {
                Id = id,
                UserId = userId,
                TokenHash = tokenHash,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt
            };
        }

        public bool IsActiveAt(DateTime occurredAt)
            => ConsumedAt == null
                && RevokedAt == null
                && occurredAt < ExpiresAt;

        public void Consume(DateTime occurredAt)
        {
            if (!IsActiveAt(occurredAt) || occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "email_verification_token_invalid",
                    "Mã xác minh email không còn hiệu lực.");
            }

            ConsumedAt = occurredAt;
        }

        public bool Revoke(DateTime occurredAt)
        {
            if (ConsumedAt.HasValue || RevokedAt.HasValue)
                return false;

            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "email_verification_token_time_invalid",
                    "Thời điểm thu hồi mã xác minh email không hợp lệ.");
            }

            RevokedAt = occurredAt;
            return true;
        }
    }
}
