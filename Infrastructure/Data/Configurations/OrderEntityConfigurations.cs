using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
    {
        public void Configure(EntityTypeBuilder<Order> builder)
        {
            builder.Property(order => order.TotalAmount).HasPrecision(18, 2);
            builder.Property(order => order.SubtotalAmount).HasPrecision(18, 2);
            builder.Property(order => order.DiscountAmount).HasPrecision(18, 2);
            builder.Property(order => order.ShippingFee).HasPrecision(18, 2);
            builder.Property(order => order.TaxAmount).HasPrecision(18, 2);
            builder.Property(order => order.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("VND");
            builder.Property(order => order.RowVersion).IsRowVersion();

            builder.HasIndex(order => order.OrderNumber).IsUnique();
            builder.HasIndex(order => new { order.UserId, order.IdempotencyKey }).IsUnique();
            builder.HasIndex(order => new { order.UserId, order.OrderDate });
            builder.HasIndex(order => new { order.UserId, order.Status, order.OrderDate });
            builder.HasIndex(order => new { order.Status, order.OrderDate });
            builder.HasIndex(order => new { order.Status, order.ExpiresAt, order.Id })
                .HasFilter("[ExpiresAt] IS NOT NULL");
            builder.HasIndex(order => order.OrderDate);

            builder.Property(order => order.ShippingAddress).HasMaxLength(500);
            builder.Property(order => order.Note).HasMaxLength(500);
            builder.Property(order => order.OrderNumber).HasMaxLength(32);
            builder.Property(order => order.IdempotencyKey).HasMaxLength(100);
            builder.Property(order => order.IdempotencyRequestHash).HasMaxLength(64);
            builder.Property(order => order.PromotionCodeSnapshot)
                .HasMaxLength(32);
            builder.Property(order => order.CancellationReason).HasMaxLength(200);

            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Orders_TotalAmount_Positive", "[TotalAmount] > 0");
                table.HasCheckConstraint("CK_Orders_Status_Valid", "[Status] BETWEEN 0 AND 6");
                table.HasCheckConstraint(
                    "CK_Orders_ShippingMethod_Valid",
                    "[ShippingMethod] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_Orders_Currency_Valid",
                    "LEN([Currency]) = 3 AND [Currency] = UPPER([Currency])");
                table.HasCheckConstraint(
                    "CK_Orders_PromotionSnapshot_Consistent",
                    "([PromotionId] IS NULL AND [PromotionCodeSnapshot] IS NULL) OR ([PromotionId] IS NOT NULL AND [PromotionCodeSnapshot] IS NOT NULL)");
                table.HasCheckConstraint(
                    "CK_Orders_Amounts_NonNegative",
                    "[SubtotalAmount] >= 0 AND [DiscountAmount] >= 0 AND [ShippingFee] >= 0 AND [TaxAmount] >= 0");
                table.HasCheckConstraint(
                    "CK_Orders_TotalAmount_Consistent",
                    "[TotalAmount] = [SubtotalAmount] - [DiscountAmount] + [ShippingFee] + [TaxAmount]");
                table.HasCheckConstraint(
                    "CK_Orders_ExpiresAt_Valid",
                    "[ExpiresAt] IS NULL OR [ExpiresAt] > [OrderDate]");
                table.HasCheckConstraint(
                    "CK_Orders_Cancellation_Consistent",
                    "([Status] = 4 AND (([CancelledAt] IS NOT NULL AND [CancellationReason] IS NOT NULL) OR ([CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL))) OR ([Status] <> 4 AND [CancelledAt] IS NULL AND [ExpiredAt] IS NULL AND [CancellationReason] IS NULL)");
                table.HasCheckConstraint(
                    "CK_Orders_Expiration_Consistent",
                    "[ExpiredAt] IS NULL OR ([ExpiresAt] IS NOT NULL AND [ExpiredAt] >= [ExpiresAt] AND [Status] = 4)");
            });

            builder.HasOne(order => order.User)
                .WithMany(user => user.Orders)
                .HasForeignKey(order => order.UserId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(order => order.Promotion)
                .WithMany(promotion => promotion.Orders)
                .HasForeignKey(order => order.PromotionId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    internal sealed class OrderDetailConfiguration : IEntityTypeConfiguration<OrderDetail>
    {
        public void Configure(EntityTypeBuilder<OrderDetail> builder)
        {
            builder.Property(detail => detail.UnitPrice).HasPrecision(18, 2);
            builder.HasIndex(detail => new { detail.OrderId, detail.ProductId })
                .HasDatabaseName("UX_OrderDetails_OrderId_ProductId")
                .IsUnique();
            builder.Property(detail => detail.ProductNameSnapshot).HasMaxLength(200);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_OrderDetails_Quantity_Positive", "[Quantity] > 0");
                table.HasCheckConstraint("CK_OrderDetails_UnitPrice_Positive", "[UnitPrice] > 0");
            });

            builder.HasOne(detail => detail.Order)
                .WithMany(order => order.OrderDetails)
                .HasForeignKey(detail => detail.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(detail => detail.Product)
                .WithMany(product => product.OrderDetails)
                .HasForeignKey(detail => detail.ProductId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    internal sealed class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
    {
        public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
        {
            builder.HasIndex(history => new { history.OrderId, history.CreatedAt });
            builder.HasIndex(history => new { history.ToStatus, history.CreatedAt, history.OrderId })
                .HasDatabaseName("IX_OrderStatusHistories_ToStatus_CreatedAt_OrderId");
            builder.HasIndex(history => new { history.OrderId, history.ToStatus })
                .HasDatabaseName("IX_OrderStatusHistories_OrderId_ToStatus");
            builder.Property(history => history.Note).HasMaxLength(500);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_OrderStatusHistories_Status_Valid",
                    "[ToStatus] BETWEEN 0 AND 6 AND ([FromStatus] IS NULL OR [FromStatus] BETWEEN 0 AND 6)");
                table.HasCheckConstraint(
                    "CK_OrderStatusHistories_Status_Changed",
                    "[FromStatus] IS NULL OR [FromStatus] <> [ToStatus]");
            });

            builder.HasOne(history => history.Order)
                .WithMany(order => order.StatusHistory)
                .HasForeignKey(history => history.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(history => history.ChangedByUser)
                .WithMany(user => user.OrderStatusChanges)
                .HasForeignKey(history => history.ChangedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
