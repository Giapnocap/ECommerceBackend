using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IAppDbContext
    {
        DbSet<User> Users { get; }
        DbSet<Role> Roles { get; }
        DbSet<UserRole> UserRoles { get; }
        DbSet<Permission> Permissions { get; }
        DbSet<RolePermission> RolePermissions { get; }
        DbSet<Category> Categories { get; }
        DbSet<Product> Products { get; }
        DbSet<ProductImage> ProductImages { get; }
        DbSet<Cart> Carts { get; }
        DbSet<CartItem> CartItems { get; }
        DbSet<Order> Orders { get; }
        DbSet<OrderDetail> OrderDetails { get; }
        DbSet<OrderStatusHistory> OrderStatusHistories { get; }
        DbSet<Payment> Payments { get; }
        DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; }
        DbSet<PaymentStatusHistory> PaymentStatusHistories { get; }
        DbSet<InventoryTransaction> InventoryTransactions { get; }
        DbSet<RefreshToken> RefreshTokens { get; }
        DbSet<OutboxMessage> OutboxMessages { get; }
        DbSet<AuditEvent> AuditEvents { get; }
        EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
