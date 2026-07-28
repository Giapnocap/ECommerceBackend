namespace ECommerceBackend.Application.Common
{
    public static class PermissionNames
    {
        public const string ManageUsers = "manage_users";
        public const string ManageProducts = "manage_products";
        public const string ManageCategories = "manage_categories";
        public const string ManageOrders = "manage_orders";
        public const string ViewReports = "view_reports";
        public const string ProcessOrders = "process_orders";
        public const string ViewInventory = "view_inventory";

        public static IReadOnlyList<string> All { get; } =
        [
            ManageUsers,
            ManageProducts,
            ManageCategories,
            ManageOrders,
            ViewReports,
            ProcessOrders,
            ViewInventory
        ];

        public static IReadOnlyList<string> StaffPermissions { get; } =
        [
            ProcessOrders,
            ViewInventory
        ];
    }
}
