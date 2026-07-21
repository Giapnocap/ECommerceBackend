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
                    "Only an active refresh token can be rotated.");
            }

            ValidateRevocation(occurredAt, "Rotated");
            if (string.IsNullOrWhiteSpace(replacementTokenHash)
                || replacementTokenHash.Length > 128
                || string.Equals(TokenHash, replacementTokenHash, StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    "refresh_token_replacement_invalid",
                    "A valid and different replacement token hash is required.");
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
                    "Refresh token revocation cannot occur before token creation.");
            }

            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 100)
            {
                throw new DomainRuleViolationException(
                    "refresh_token_revocation_reason_invalid",
                    "Refresh token revocation reason must contain between 1 and 100 characters.");
            }
        }
    }
}