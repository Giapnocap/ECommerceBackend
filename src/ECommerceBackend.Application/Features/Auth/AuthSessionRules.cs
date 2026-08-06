using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    internal static class AuthSessionRules
    {
        public static string Normalize(string value)
            => value.Trim().ToUpperInvariant();

        public static IEnumerable<string> GetRoles(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .Select(userRole => userRole.Role!.Name);

        public static IEnumerable<string> GetPermissions(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .SelectMany(userRole => userRole.Role!.RolePermissions)
                .Where(rolePermission => rolePermission.Permission != null)
                .Select(rolePermission => rolePermission.Permission!.Name);

        public static void RevokeTokens(
            IEnumerable<RefreshToken> tokens,
            string reason,
            DateTime occurredAt)
        {
            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() =>
                    token.Revoke(occurredAt, reason));
            }
        }

        public static ApiException Unauthorized()
            => new(
                401,
                "unauthorized",
                "Tên đăng nhập, mật khẩu hoặc token không hợp lệ.");

        public static ConflictException SessionConflict(Exception inner)
            => new(
                "session_concurrency_conflict",
                "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                inner);
    }
}
