using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class UserRole
    {
        public Guid UserId { get; internal set; }
        public Guid RoleId { get; internal set; }

        // Navigation
        public User? User { get; set; }
        public Role? Role { get; set; }

        public static UserRole Create(Guid userId, Role role)
        {
            ArgumentNullException.ThrowIfNull(role);
            if (userId == Guid.Empty || role.Id == Guid.Empty
                || string.IsNullOrWhiteSpace(role.Name))
            {
                throw new DomainRuleViolationException(
                    "user_role_invalid",
                    "Thông tin gán vai trò không hợp lệ.");
            }

            return new UserRole
            {
                UserId = userId,
                RoleId = role.Id,
                Role = role
            };
        }
    }
}
