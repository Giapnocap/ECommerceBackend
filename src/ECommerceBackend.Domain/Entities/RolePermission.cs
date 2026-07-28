namespace ECommerceBackend.Domain.Entities
{
    public class RolePermission
    {
        public Guid RoleId { get; set; }
        public Guid PermissionId { get; set; }

        // Navigation
        public Role? Role { get; set; }
        public Permission? Permission { get; set; }
    }
}
