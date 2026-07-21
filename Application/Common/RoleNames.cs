namespace ECommerceBackend.Application.Common
{
    public static class RoleNames
    {
        public const string Admin = "Admin";
        public const string Staff = "Staff";
        public const string Customer = "Customer";

        public static IReadOnlyList<string> All { get; } = [Admin, Staff, Customer];

        public static bool IsValid(string? roleName)
            => roleName != null && All.Contains(roleName, StringComparer.Ordinal);
    }
}
