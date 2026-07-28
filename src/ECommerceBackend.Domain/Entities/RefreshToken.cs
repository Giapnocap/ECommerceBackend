using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class RefreshToken
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid FamilyId { get; set; }
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; private set; }
        public string? RevocationReason { get; private set; }
        public string? ReplacedByTokenHash { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public User? User { get; set; }

        public bool IsRevoked => RevokedAt.HasValue;

        public bool IsExpiredAt(DateTime utcNow) => utcNow >= ExpiresAt;

        public bool IsActiveAt(DateTime utcNow) => !RevokedAt.HasValue && !IsExpiredAt(utcNow);

        public bool Revoke(DateTime occurredAt, string reason)
        {
            if (RevokedAt.HasValue)
                return false;

            ValidateRevocation(occurredAt, reason);
            RevokedAt = occurredAt;
            RevocationReason = reason.Trim();
            ReplacedByTokenHash = null;
            return true;
        }

        public void Rotate(DateTime occurredAt, string replacementTokenHash)
        {
            if (RevokedAt.HasValue || IsExpiredAt(occurredAt))
            {
                throw new DomainRuleViolationException(
                    "refresh_token_not_active",
                    "Chỉ mã làm mới phiên còn hiệu lực mới có thể được luân chuyển.");
            }

            ValidateRevocation(occurredAt, "Rotated");
            if (string.IsNullOrWhiteSpace(replacementTokenHash)
                || replacementTokenHash.Length > 128
                || string.Equals(TokenHash, replacementTokenHash, StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    "refresh_token_replacement_invalid",
                    "Giá trị băm của mã thay thế phải hợp lệ và khác mã hiện tại.");
            }

            RevokedAt = occurredAt;
            RevocationReason = "Rotated";
            ReplacedByTokenHash = replacementTokenHash;
        }

        private void ValidateRevocation(DateTime occurredAt, string reason)
        {
            if (occurredAt < CreatedAt)
            {
                throw new DomainRuleViolationException(
                    "refresh_token_revocation_time_invalid",
                    "Thời điểm thu hồi mã làm mới phiên không được trước thời điểm tạo mã.");
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 100)
            {
                throw new DomainRuleViolationException(
                    "refresh_token_revocation_reason_invalid",
                    "Lý do thu hồi mã làm mới phiên phải có từ 1 đến 100 ký tự.");
            }
        }
    }
}
