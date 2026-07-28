using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class PasswordResetToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? ConsumedAt { get; private set; }
        public DateTime? RevokedAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public User? User { get; set; }

        public bool IsExpiredAt(DateTime occurredAt) => occurredAt >= ExpiresAt;

        public bool IsActiveAt(DateTime occurredAt)
            => !ConsumedAt.HasValue
                && !RevokedAt.HasValue
                && !IsExpiredAt(occurredAt);

        public void Consume(DateTime occurredAt)
        {
            if (!IsActiveAt(occurredAt))
            {
                throw new DomainRuleViolationException(
                    "password_reset_token_not_active",
                    "Mã đặt lại mật khẩu không còn hiệu lực.");
            }

            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "password_reset_token_time_invalid",
                    "Thời điểm sử dụng mã đặt lại mật khẩu không hợp lệ.");
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
                    "password_reset_token_time_invalid",
                    "Thời điểm thu hồi mã đặt lại mật khẩu không hợp lệ.");
            }

            RevokedAt = occurredAt;
            return true;
        }
    }
}
