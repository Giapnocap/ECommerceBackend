using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class PromotionConfiguration
        : IEntityTypeConfiguration<Promotion>
    {
        public void Configure(EntityTypeBuilder<Promotion> builder)
        {
            builder.Property(promotion => promotion.Code)
                .HasMaxLength(32);
            builder.Property(promotion => promotion.NormalizedCode)
                .HasMaxLength(32);
            builder.Property(promotion => promotion.Value)
                .HasPrecision(18, 2);
            builder.Property(promotion => promotion.MinimumSubtotal)
                .HasPrecision(18, 2);
            builder.Property(promotion => promotion.MaximumDiscountAmount)
                .HasPrecision(18, 2);
            builder.Property(promotion => promotion.RowVersion)
                .IsRowVersion();

            builder.HasIndex(promotion => promotion.NormalizedCode)
                .HasDatabaseName("UX_Promotions_NormalizedCode")
                .IsUnique();
            builder.HasIndex(
                    promotion => new
                    {
                        promotion.IsActive,
                        promotion.StartsAt,
                        promotion.EndsAt
                    })
                .HasDatabaseName(
                    "IX_Promotions_IsActive_StartsAt_EndsAt");

            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Promotions_Type_Valid",
                    "[Type] BETWEEN 0 AND 1");
                table.HasCheckConstraint(
                    "CK_Promotions_Value_Valid",
                    "[Value] > 0 AND ([Type] <> 1 OR [Value] <= 100)");
                table.HasCheckConstraint(
                    "CK_Promotions_Amounts_Valid",
                    "[MinimumSubtotal] >= 0 AND ([MaximumDiscountAmount] IS NULL OR [MaximumDiscountAmount] > 0)");
                table.HasCheckConstraint(
                    "CK_Promotions_MaxDiscount_Compatible",
                    "[Type] = 1 OR [MaximumDiscountAmount] IS NULL");
                table.HasCheckConstraint(
                    "CK_Promotions_Period_Valid",
                    "[StartsAt] < [EndsAt]");
                table.HasCheckConstraint(
                    "CK_Promotions_Usage_Valid",
                    "[UsageLimit] > 0 AND [UsageLimitPerCustomer] > 0 AND [UsedCount] >= 0 AND [UsedCount] <= [UsageLimit]");
            });
        }
    }

    internal sealed class PromotionRedemptionConfiguration
        : IEntityTypeConfiguration<PromotionRedemption>
    {
        public void Configure(
            EntityTypeBuilder<PromotionRedemption> builder)
        {
            builder.Property(redemption => redemption.DiscountAmount)
                .HasPrecision(18, 2);
            builder.HasIndex(redemption => redemption.OrderId)
                .HasDatabaseName(
                    "UX_PromotionRedemptions_OrderId")
                .IsUnique();
            builder.HasIndex(
                    redemption => new
                    {
                        redemption.PromotionId,
                        redemption.UserId,
                        redemption.CreatedAt
                    })
                .HasDatabaseName(
                    "IX_PromotionRedemptions_PromotionId_UserId_CreatedAt");
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_PromotionRedemptions_Discount_Positive",
                    "[DiscountAmount] > 0");
            });

            builder.HasOne(redemption => redemption.Promotion)
                .WithMany(promotion => promotion.Redemptions)
                .HasForeignKey(redemption => redemption.PromotionId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasOne(redemption => redemption.Order)
                .WithOne(order => order.PromotionRedemption)
                .HasForeignKey<PromotionRedemption>(
                    redemption => redemption.OrderId)
                .OnDelete(DeleteBehavior.NoAction);
            builder.HasOne(redemption => redemption.User)
                .WithMany()
                .HasForeignKey(redemption => redemption.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
