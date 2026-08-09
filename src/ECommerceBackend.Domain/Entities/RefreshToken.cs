using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class RefreshToken
    {
        internal RefreshToken()
        {
        }

        public Guid Id { get; internal set; }
        public Guid UserId { get; internal set; }
        public Guid FamilyId { get; internal set; }
        public string TokenHash { get; internal set; } = string.Empty;
        public DateTime ExpiresAt { get; internal set; }
        public DateTime CreatedAt { get; internal set; }
        public DateTime? RevokedAt { get; private set; }
        public string? RevocationReason { get; private set; }
        public string? ReplacedByTokenHash { get; private set; }
        public byte[] RowVersion { get; internal set; } = [];

        public User? User { get; set; }

        public bool IsRevoked => RevokedAt.HasValue;

        public bool IsExpiredAt(DateTime utcNow) => utcNow >= ExpiresAt;

        public bool IsActiveAt(DateTime utcNow) => !RevokedAt.HasValue && !IsExpiredAt(utcNow);

        public static RefreshToken Create(
            Guid id,
            Guid userId,
            Guid familyId,
            string tokenHash,
            DateTime createdAt,
            DateTime expiresAt)
        {
            if (id == Guid.Empty || userId == Guid.Empty || familyId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "refresh_token_identity_invalid",
                    "Thông tin định danh của mã làm mới phiên không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(tokenHash)
                || tokenHash.Length > 128
                || expiresAt <= createdAt)
            {
                throw new DomainRuleViolationException(
                    "refresh_token_details_invalid",
                    "Thông tin thời hạn hoặc giá trị băm của mã làm mới phiên không hợp lệ.");
            }

            return new RefreshToken
            {
                Id = id,
                UserId = userId,
                FamilyId = familyId,
                TokenHash = tokenHash,
                CreatedAt = createdAt,
                ExpiresAt = expiresAt
            };
        }

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
