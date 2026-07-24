using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class User
    {
        public Guid Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string NormalizedUserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string NormalizedEmail { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public bool IsDeleted { get; set; }
        public int TokenVersion { get; private set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? PasswordChangedAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
        public Cart? Cart { get; set; }
        public ICollection<Order> Orders { get; set; } = new List<Order>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<OrderStatusHistory> OrderStatusChanges { get; set; } = new List<OrderStatusHistory>();
        public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

        public void ChangePasswordHash(string passwordHash, DateTime occurredAt)
        {
            var normalizedHash = ValidatePasswordHash(passwordHash);
            if (string.Equals(PasswordHash, normalizedHash, StringComparison.Ordinal))
            {
                throw new DomainRuleViolationException(
                    "user_password_hash_unchanged",
                    "Giá trị băm của mật khẩu mới phải khác mật khẩu hiện tại.");
            }

            PasswordHash = normalizedHash;
            PasswordChangedAt = occurredAt;
            InvalidateSessions();
        }

        public void InvalidateSessions()
        {
            try
            {
                TokenVersion = checked(TokenVersion + 1);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "user_token_version_exceeded",
                    "Phiên bản mã xác thực của người dùng vượt quá giới hạn cho phép.");
            }
        }

        private static string ValidatePasswordHash(string passwordHash)
        {
            if (string.IsNullOrWhiteSpace(passwordHash) || passwordHash.Length > 200)
            {
                throw new DomainRuleViolationException(
                    "user_password_hash_invalid",
                    "Giá trị băm của mật khẩu phải có từ 1 đến 200 ký tự.");
            }

            return passwordHash;
        }
    }
}
