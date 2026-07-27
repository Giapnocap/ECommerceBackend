using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data
{
    public class AppDbContext : DbContext, IUnitOfWork
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<RolePermission> RolePermissions { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<ProductImage> ProductImages { get; set; }
        public DbSet<Cart> Carts { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderDetail> OrderDetails { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; set; }
        public DbSet<PaymentStatusHistory> PaymentStatusHistories { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }
        public DbSet<OutboxMessage> OutboxMessages { get; set; }
        public DbSet<AuditEvent> AuditEvents { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Soft delete is applied explicitly in services so required relationships
            // such as OrderDetail -> Product can still preserve historical data.

            // ===================== COMPOSITE KEYS =====================
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>().HasIndex(ur => ur.UserId).IsUnique();
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

            // ===================== DECIMAL PRECISION =====================
            modelBuilder.Entity<Product>().Property(p => p.Price).HasPrecision(18, 2);
            modelBuilder.Entity<CartItem>().Property(ci => ci.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.TotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.SubtotalAmount).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.DiscountAmount).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.ShippingFee).HasPrecision(18, 2);
            modelBuilder.Entity<Order>().Property(o => o.TaxAmount).HasPrecision(18, 2);
            modelBuilder.Entity<OrderDetail>().Property(od => od.UnitPrice).HasPrecision(18, 2);
            modelBuilder.Entity<Payment>().Property(payment => payment.Amount).HasPrecision(18, 2);

            // ===================== CONCURRENCY =====================
            modelBuilder.Entity<User>().Property(u => u.RowVersion).IsRowVersion();
            modelBuilder.Entity<Category>().Property(c => c.RowVersion).IsRowVersion();
            modelBuilder.Entity<Product>().Property(p => p.RowVersion).IsRowVersion();
            modelBuilder.Entity<Order>().Property(o => o.RowVersion).IsRowVersion();
            modelBuilder.Entity<Payment>().Property(payment => payment.RowVersion).IsRowVersion();
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.RowVersion).IsRowVersion();
            modelBuilder.Entity<PasswordResetToken>()
                .Property(token => token.RowVersion)
                .IsRowVersion();

            // ===================== UNIQUE INDEXES =====================
            modelBuilder.Entity<User>().HasIndex(u => u.NormalizedUserName).IsUnique();
            modelBuilder.Entity<User>().HasIndex(u => u.NormalizedEmail).IsUnique();
            modelBuilder.Entity<Role>().HasIndex(r => r.Name).IsUnique();
            modelBuilder.Entity<Permission>().HasIndex(p => p.Name).IsUnique();
            modelBuilder.Entity<Category>()
                .HasIndex(c => c.NormalizedName)
                .HasDatabaseName("UX_Categories_Root_NormalizedName")
                .HasFilter("[IsDeleted] = 0 AND [ParentId] IS NULL")
                .IsUnique();
            modelBuilder.Entity<Category>()
                .HasIndex(c => new { c.ParentId, c.NormalizedName })
                .HasDatabaseName("UX_Categories_ParentId_NormalizedName")
                .HasFilter("[IsDeleted] = 0 AND [ParentId] IS NOT NULL")
                .IsUnique();
            modelBuilder.Entity<Cart>().HasIndex(c => c.UserId).IsUnique(); // 1 user - 1 cart
            modelBuilder.Entity<CartItem>()
                .HasIndex(ci => new { ci.CartId, ci.ProductId })
                .HasDatabaseName("UX_CartItems_CartId_ProductId")
                .IsUnique();
            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.IsDeleted, product.StockQuantity });
            modelBuilder.Entity<Product>()
                .HasIndex(product => new { product.IsDeleted, product.CreatedAt, product.Id })
                .HasDatabaseName("IX_Products_IsDeleted_CreatedAt_Id")
                .IsDescending(false, true, true);
            modelBuilder.Entity<ProductImage>()
                .HasIndex(pi => pi.ProductId)
                .HasDatabaseName("UX_ProductImages_ProductId_IsMain")
                .HasFilter("[IsMain] = 1")
                .IsUnique();
            modelBuilder.Entity<RefreshToken>().HasIndex(rt => rt.TokenHash).IsUnique();
            modelBuilder.Entity<RefreshToken>()
                .HasIndex(rt => rt.ExpiresAt)
                .HasDatabaseName("IX_RefreshTokens_ExpiresAt");
            modelBuilder.Entity<RefreshToken>().HasIndex(rt => new { rt.UserId, rt.ExpiresAt });
            modelBuilder.Entity<RefreshToken>().HasIndex(rt => new { rt.UserId, rt.FamilyId, rt.ExpiresAt });
            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.TokenHash)
                .IsUnique();
            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.ExpiresAt);
            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.UserId)
                .HasDatabaseName("UX_PasswordResetTokens_UserId_Active")
                .HasFilter("[ConsumedAt] IS NULL AND [RevokedAt] IS NULL")
                .IsUnique();
            modelBuilder.Entity<Order>().HasIndex(order => order.OrderNumber).IsUnique();
            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.UserId, order.IdempotencyKey })
                .IsUnique();
            modelBuilder.Entity<Order>().HasIndex(order => new { order.UserId, order.OrderDate });
            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.UserId, order.Status, order.OrderDate });
            modelBuilder.Entity<Order>().HasIndex(order => new { order.Status, order.OrderDate });
            modelBuilder.Entity<Order>()
                .HasIndex(order => new { order.Status, order.ExpiresAt, order.Id })
                .HasFilter("[ExpiresAt] IS NOT NULL");
            modelBuilder.Entity<Order>().HasIndex(order => order.OrderDate);
            modelBuilder.Entity<OrderDetail>()
                .HasIndex(detail => new { detail.OrderId, detail.ProductId })
                .HasDatabaseName("UX_OrderDetails_OrderId_ProductId")
                .IsUnique();
            modelBuilder.Entity<OrderStatusHistory>().HasIndex(history => new { history.OrderId, history.CreatedAt });
            modelBuilder.Entity<OrderStatusHistory>()
                .HasIndex(history => new { history.ToStatus, history.CreatedAt, history.OrderId })
                .HasDatabaseName("IX_OrderStatusHistories_ToStatus_CreatedAt_OrderId");
            modelBuilder.Entity<OrderStatusHistory>()
                .HasIndex(history => new { history.OrderId, history.ToStatus })
                .HasDatabaseName("IX_OrderStatusHistories_OrderId_ToStatus");
            modelBuilder.Entity<Payment>().HasIndex(payment => payment.OrderId).IsUnique();
            modelBuilder.Entity<Payment>().HasIndex(payment => new { payment.Status, payment.CreatedAt });
            modelBuilder.Entity<Payment>()
                .HasIndex(payment => payment.PaidAt)
                .HasFilter("[PaidAt] IS NOT NULL");
            modelBuilder.Entity<Payment>()
                .HasIndex(payment => new { payment.Provider, payment.ProviderTransactionId })
                .HasFilter("[Provider] IS NOT NULL AND [ProviderTransactionId] IS NOT NULL")
                .IsUnique();
            modelBuilder.Entity<PaymentWebhookEvent>()
                .HasIndex(webhook => new { webhook.Provider, webhook.ProviderEventId })
                .IsUnique();
            modelBuilder.Entity<PaymentWebhookEvent>()
                .HasIndex(webhook => new { webhook.PaymentId, webhook.ReceivedAt });
            modelBuilder.Entity<PaymentWebhookEvent>()
                .HasIndex(webhook => webhook.ReceivedAt)
                .HasDatabaseName("IX_PaymentWebhookEvents_ReceivedAt");
            modelBuilder.Entity<PaymentStatusHistory>()
                .HasIndex(history => new { history.PaymentId, history.CreatedAt });
            modelBuilder.Entity<PaymentStatusHistory>()
                .HasIndex(history => new { history.ToStatus, history.OccurredAt });
            modelBuilder.Entity<PaymentStatusHistory>()
                .HasIndex(history => new { history.PaymentId, history.ToStatus })
                .HasDatabaseName("UX_PaymentStatusHistories_PaymentId_ToStatus")
                .IsUnique();
            modelBuilder.Entity<InventoryTransaction>()
                .HasIndex(transaction => new { transaction.ProductId, transaction.CreatedAt });
            modelBuilder.Entity<InventoryTransaction>().HasIndex(transaction => transaction.OrderId);
            modelBuilder.Entity<InventoryTransaction>()
                .HasIndex(transaction => new { transaction.OrderId, transaction.ProductId, transaction.Type })
                .HasDatabaseName("UX_InventoryTransactions_OrderId_ProductId_Type")
                .HasFilter("[OrderId] IS NOT NULL")
                .IsUnique();
            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(message => new
                {
                    message.NextAttemptAt,
                    message.LockedAt,
                    message.OccurredAt
                })
                .HasDatabaseName("IX_OutboxMessages_Ready")
                .HasFilter("[ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL");
            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(message => message.DeadLetteredAt)
                .HasDatabaseName("IX_OutboxMessages_DeadLetteredAt")
                .HasFilter("[DeadLetteredAt] IS NOT NULL");
            modelBuilder.Entity<OutboxMessage>()
                .HasIndex(message => message.ProcessedAt)
                .HasDatabaseName("IX_OutboxMessages_ProcessedAt")
                .HasFilter("[ProcessedAt] IS NOT NULL");
            modelBuilder.Entity<AuditEvent>()
                .HasIndex(audit => new { audit.CreatedAt, audit.Id });
            modelBuilder.Entity<AuditEvent>()
                .HasIndex(audit => new { audit.ActorUserId, audit.CreatedAt });
            modelBuilder.Entity<AuditEvent>()
                .HasIndex(audit => new { audit.EntityType, audit.EntityId, audit.CreatedAt });
            modelBuilder.Entity<AuditEvent>().HasIndex(audit => audit.CorrelationId);

            // ===================== STRING LENGTHS =====================
            modelBuilder.Entity<User>().Property(u => u.UserName).HasMaxLength(50);
            modelBuilder.Entity<User>().Property(u => u.NormalizedUserName).HasMaxLength(50);
            modelBuilder.Entity<User>().Property(u => u.Email).HasMaxLength(254);
            modelBuilder.Entity<User>().Property(u => u.NormalizedEmail).HasMaxLength(254);
            modelBuilder.Entity<User>().Property(u => u.PasswordHash).HasMaxLength(200);
            modelBuilder.Entity<User>().Property(u => u.FullName).HasMaxLength(100);
            modelBuilder.Entity<User>().Property(u => u.Phone).HasMaxLength(20);
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.TokenHash).HasMaxLength(128);
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.ReplacedByTokenHash).HasMaxLength(128);
            modelBuilder.Entity<RefreshToken>().Property(rt => rt.RevocationReason).HasMaxLength(100);
            modelBuilder.Entity<PasswordResetToken>()
                .Property(token => token.TokenHash)
                .HasMaxLength(64);
            modelBuilder.Entity<Category>().Property(c => c.Name).HasMaxLength(100);
            modelBuilder.Entity<Category>().Property(c => c.NormalizedName).HasMaxLength(100);
            modelBuilder.Entity<Product>().Property(p => p.Name).HasMaxLength(200);
            modelBuilder.Entity<Product>().Property(p => p.Description).HasMaxLength(2000);
            modelBuilder.Entity<ProductImage>().Property(pi => pi.ImageUrl).HasMaxLength(500);
            modelBuilder.Entity<Order>().Property(o => o.ShippingAddress).HasMaxLength(500);
            modelBuilder.Entity<Order>().Property(o => o.Note).HasMaxLength(500);
            modelBuilder.Entity<Order>().Property(o => o.OrderNumber).HasMaxLength(32);
            modelBuilder.Entity<Order>().Property(o => o.IdempotencyKey).HasMaxLength(100);
            modelBuilder.Entity<Order>().Property(o => o.IdempotencyRequestHash).HasMaxLength(64);
            modelBuilder.Entity<Order>().Property(o => o.CancellationReason).HasMaxLength(200);
            modelBuilder.Entity<OrderDetail>().Property(detail => detail.ProductNameSnapshot).HasMaxLength(200);
            modelBuilder.Entity<OrderStatusHistory>().Property(history => history.Note).HasMaxLength(500);
            modelBuilder.Entity<Payment>().Property(payment => payment.Provider).HasMaxLength(100);
            modelBuilder.Entity<Payment>().Property(payment => payment.ProviderTransactionId).HasMaxLength(200);
            modelBuilder.Entity<PaymentWebhookEvent>().Property(webhook => webhook.Provider).HasMaxLength(100);
            modelBuilder.Entity<PaymentWebhookEvent>().Property(webhook => webhook.ProviderEventId).HasMaxLength(200);
            modelBuilder.Entity<PaymentWebhookEvent>().Property(webhook => webhook.PayloadHash).HasMaxLength(64);
            modelBuilder.Entity<PaymentStatusHistory>().Property(history => history.Reference).HasMaxLength(200);
            modelBuilder.Entity<InventoryTransaction>().Property(transaction => transaction.Reason).HasMaxLength(500);
            modelBuilder.Entity<OutboxMessage>().Property(message => message.Type).HasMaxLength(200);
            modelBuilder.Entity<OutboxMessage>().Property(message => message.LastError).HasMaxLength(2000);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.Action).HasMaxLength(100);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.EntityType).HasMaxLength(100);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.EntityId).HasMaxLength(100);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.CorrelationId).HasMaxLength(128);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.IpAddress).HasMaxLength(45);
            modelBuilder.Entity<AuditEvent>().Property(audit => audit.MetadataJson).HasMaxLength(4000);

            // ===================== CHECK CONSTRAINTS =====================
            modelBuilder.Entity<Product>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_Products_Price_Positive", "[Price] > 0");
                t.HasCheckConstraint("CK_Products_Stock_NonNegative", "[StockQuantity] >= 0");
            });

            modelBuilder.Entity<Category>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_Categories_Parent_NotSelf", "[ParentId] IS NULL OR [ParentId] <> [Id]");
            });

            modelBuilder.Entity<CartItem>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
                t.HasCheckConstraint("CK_CartItems_UnitPrice_Positive", "[UnitPrice] > 0");
            });

            modelBuilder.Entity<Order>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_Orders_TotalAmount_Positive", "[TotalAmount] > 0");
                t.HasCheckConstraint("CK_Orders_Status_Valid", "[Status] BETWEEN 0 AND 6");
                t.HasCheckConstraint("CK_Orders_Amounts_NonNegative", "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingFee] >= 0 AND [TaxAmount] >= 0");
                t.HasCheckConstraint("CK_Orders_TotalAmount_Consistent", "[TotalAmount] = [SubtotalAmount] - [DiscountAmount] + [ShippingFee] + [TaxAmount]");
                t.HasCheckConstraint("CK_Orders_ExpiresAt_Valid", "[ExpiresAt] IS NULL OR [ExpiresAt] > [OrderDate]");
                t.HasCheckConstraint("CK_Orders_Cancellation_Consistent", "([Status] = 4 AND (([CancelledAt] IS NOT NULL AND [CancellationReason] IS NOT NULL) OR ([CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL))) OR ([Status] <> 4 AND [CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL)");
                t.HasCheckConstraint("CK_Orders_Expiration_Consistent", "[ExpiredAt] IS NULL OR ([ExpiresAt] IS NOT NULL AND [ExpiredAt] >= [ExpiresAt] AND [Status] = 4)");
            });

            modelBuilder.Entity<OrderDetail>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_OrderDetails_Quantity_Positive", "[Quantity] > 0");
                t.HasCheckConstraint("CK_OrderDetails_UnitPrice_Positive", "[UnitPrice] > 0");
            });

            modelBuilder.Entity<OrderStatusHistory>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_OrderStatusHistories_Status_Valid", "[ToStatus] BETWEEN 0 AND 6 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 6)");
                t.HasCheckConstraint("CK_OrderStatusHistories_Status_Changed", "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
            });

            modelBuilder.Entity<Payment>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_Payments_Amount_Positive", "[Amount] > 0");
                t.HasCheckConstraint("CK_Payments_Method_Valid", "[Method] BETWEEN 0 AND 0");
                t.HasCheckConstraint("CK_Payments_Status_Valid", "[Status] BETWEEN 0 AND 4");
                t.HasCheckConstraint(
                    "CK_Payments_PaidAt_MatchesStatus",
                    "([Status] IN (1, 4) AND [PaidAt] IS NOT NULL) OR ([Status] IN (0, 2, 3) AND [PaidAt] IS NULL)");
            });

            modelBuilder.Entity<PaymentWebhookEvent>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_PaymentWebhookEvents_ResultingStatus_Valid", "[ResultingStatus] BETWEEN 0 AND 4");
                t.HasCheckConstraint("CK_PaymentWebhookEvents_PayloadHash_Length", "LEN([PayloadHash]) = 64");
            });

            modelBuilder.Entity<PaymentStatusHistory>().ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_PaymentStatusHistories_Status_Valid",
                    "[ToStatus] BETWEEN 0 AND 4 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 4)");
                t.HasCheckConstraint("CK_PaymentStatusHistories_Status_Changed", "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
                t.HasCheckConstraint("CK_PaymentStatusHistories_Source_Valid", "[Source] BETWEEN 0 AND 4");
            });

            modelBuilder.Entity<InventoryTransaction>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_InventoryTransactions_QuantityChange_NotZero", "[QuantityChange] <> 0");
                t.HasCheckConstraint("CK_InventoryTransactions_Balance_NonNegative", "[BalanceAfter] >= 0");
                t.HasCheckConstraint("CK_InventoryTransactions_Type_Valid", "[Type] BETWEEN 0 AND 4");
                t.HasCheckConstraint(
                    "CK_InventoryTransactions_QuantityChange_MatchesType",
                    "([Type] = 0 AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] IN (3, 4) AND [QuantityChange] > 0)");
                t.HasCheckConstraint(
                    "CK_InventoryTransactions_OrderLink_MatchesType",
                    "([Type] IN (0, 1) AND [OrderId] IS NULL) OR ([Type] IN (2, 3, 4) AND [OrderId] IS NOT NULL)");
            });

            modelBuilder.Entity<OutboxMessage>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_OutboxMessages_Attempts_NonNegative", "[Attempts] >= 0");
                t.HasCheckConstraint(
                    "CK_OutboxMessages_Lock_Consistent",
                    "([LockId] IS NULL AND [LockedAt] IS NULL) OR ([LockId] IS NOT NULL AND [LockedAt] IS NOT NULL)");
                t.HasCheckConstraint(
                    "CK_OutboxMessages_TerminalState_Exclusive",
                    "[ProcessedAt] IS NULL OR [DeadLetteredAt] IS NULL");
                t.HasCheckConstraint(
                    "CK_OutboxMessages_TerminalState_Unlocked",
                    "([ProcessedAt] IS NULL AND [DeadLetteredAt] IS NULL) OR ([LockId] IS NULL AND [LockedAt] IS NULL)");
            });

            modelBuilder.Entity<AuditEvent>().ToTable(t =>
            {
                t.HasCheckConstraint("CK_AuditEvents_Action_NotEmpty", "LEN([Action]) > 0");
                t.HasCheckConstraint("CK_AuditEvents_EntityType_NotEmpty", "LEN([EntityType]) > 0");
                t.HasCheckConstraint("CK_AuditEvents_CorrelationId_NotEmpty", "LEN([CorrelationId]) > 0");
            });

            modelBuilder.Entity<User>().ToTable(t =>
            {
                t.HasCheckConstraint(
                    "CK_Users_FailedLoginCount_NonNegative",
                    "[FailedLoginCount] >= 0");
            });

            // ===================== RELATIONSHIPS =====================

            // UserRole
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(token => token.User)
                .WithMany(user => user.PasswordResetTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            // RolePermission
            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<RolePermission>()
                .HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Category self-reference
            modelBuilder.Entity<Category>()
                .HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.NoAction);

            // Product → Category
            modelBuilder.Entity<Product>()
                .HasOne(p => p.Category)
                .WithMany(c => c.Products)
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.NoAction);

            // ProductImage → Product
            modelBuilder.Entity<ProductImage>()
                .HasOne(pi => pi.Product)
                .WithMany(p => p.Images)
                .HasForeignKey(pi => pi.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cart → User (1-1)
            modelBuilder.Entity<Cart>()
                .HasOne(c => c.User)
                .WithOne(u => u.Cart)
                .HasForeignKey<Cart>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // CartItem → Cart
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Cart)
                .WithMany(c => c.CartItems)
                .HasForeignKey(ci => ci.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            // CartItem → Product
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Product)
                .WithMany(p => p.CartItems)
                .HasForeignKey(ci => ci.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            // Order → User
            modelBuilder.Entity<Order>()
                .HasOne(o => o.User)
                .WithMany(u => u.Orders)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            // OrderDetail → Order
            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Order)
                .WithMany(o => o.OrderDetails)
                .HasForeignKey(od => od.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // OrderDetail → Product
            modelBuilder.Entity<OrderDetail>()
                .HasOne(od => od.Product)
                .WithMany(p => p.OrderDetails)
                .HasForeignKey(od => od.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            // OrderStatusHistory -> Order / User
            modelBuilder.Entity<OrderStatusHistory>()
                .HasOne(history => history.Order)
                .WithMany(order => order.StatusHistory)
                .HasForeignKey(history => history.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OrderStatusHistory>()
                .HasOne(history => history.ChangedByUser)
                .WithMany(user => user.OrderStatusChanges)
                .HasForeignKey(history => history.ChangedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            // Payment -> Order (1-1)
            modelBuilder.Entity<Payment>()
                .HasOne(payment => payment.Order)
                .WithOne(order => order.Payment)
                .HasForeignKey<Payment>(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PaymentWebhookEvent>()
                .HasOne(webhook => webhook.Payment)
                .WithMany(payment => payment.WebhookEvents)
                .HasForeignKey(webhook => webhook.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PaymentStatusHistory>()
                .HasOne(history => history.Payment)
                .WithMany(payment => payment.StatusHistory)
                .HasForeignKey(history => history.PaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PaymentStatusHistory>()
                .HasOne<User>()
                .WithMany()
                .HasForeignKey(history => history.ChangedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            // InventoryTransaction -> Product / Order / User
            modelBuilder.Entity<InventoryTransaction>()
                .HasOne(transaction => transaction.Product)
                .WithMany(product => product.InventoryTransactions)
                .HasForeignKey(transaction => transaction.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<InventoryTransaction>()
                .HasOne(transaction => transaction.Order)
                .WithMany(order => order.InventoryTransactions)
                .HasForeignKey(transaction => transaction.OrderId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<InventoryTransaction>()
                .HasOne(transaction => transaction.CreatedByUser)
                .WithMany(user => user.InventoryTransactions)
                .HasForeignKey(transaction => transaction.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            // RefreshToken → User
            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ===================== SEED DATA =====================
            SeedData(modelBuilder);
        }

        private static void SeedData(ModelBuilder modelBuilder)
        {
            // Seed Roles
            var adminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var staffRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var customerRoleId = Guid.Parse("33333333-3333-3333-3333-333333333333");

            modelBuilder.Entity<Role>().HasData(
                new Role { Id = adminRoleId, Name = RoleNames.Admin },
                new Role { Id = staffRoleId, Name = RoleNames.Staff },
                new Role { Id = customerRoleId, Name = RoleNames.Customer }
            );

            // Seed Permissions
            var permissions = PermissionNames.All;

            var permEntities = permissions.Select((name, i) => new Permission
            {
                Id = Guid.Parse($"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaa{i + 1:D3}"),
                Name = name
            }).ToArray();

            modelBuilder.Entity<Permission>().HasData(permEntities);

            // Admin gets all permissions
            var adminPermissions = permEntities.Select(p => new RolePermission
            {
                RoleId = adminRoleId,
                PermissionId = p.Id
            }).ToArray();
            modelBuilder.Entity<RolePermission>().HasData(adminPermissions);

            // Staff can process orders and inspect inventory.
            var staffPermissions = PermissionNames.StaffPermissions
                .Select(permissionName => new RolePermission
                {
                    RoleId = staffRoleId,
                    PermissionId = permEntities.Single(permission => permission.Name == permissionName).Id
                })
                .ToArray();
            modelBuilder.Entity<RolePermission>().HasData(staffPermissions);

        }
    }
}
