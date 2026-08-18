using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class InventoryTransactionConfiguration :
        IEntityTypeConfiguration<InventoryTransaction>
    {
        public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
        {
            builder.HasIndex(transaction => new { transaction.ProductId, transaction.CreatedAt });
            builder.HasIndex(transaction => transaction.OrderId);
            builder.HasIndex(transaction => new
            {
                transaction.OrderId,
                transaction.ProductId,
                transaction.Type
            })
                .HasDatabaseName("UX_InventoryTransactions_OrderId_ProductId_Type")
                .HasFilter("[OrderId] IS NOT NULL")
                .IsUnique();
            builder.Property(transaction => transaction.Reason).HasMaxLength(500);
            builder.Property(transaction => transaction.Reference).HasMaxLength(200);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_InventoryTransactions_QuantityChange_NotZero",
                    "[QuantityChange] <> 0");
                table.HasCheckConstraint(
                    "CK_InventoryTransactions_Balance_NonNegative",
                    "[BalanceAfter] >= 0");
                table.HasCheckConstraint(
                    "CK_InventoryTransactions_Type_Valid",
                    "[Type] BETWEEN 0 AND 5");
                table.HasCheckConstraint(
                    "CK_InventoryTransactions_QuantityChange_MatchesType",
                    "([Type] IN (0, 5) AND [QuantityChange] > 0) OR ([Type] = 1 AND [QuantityChange] <> 0) OR ([Type] = 2 AND [QuantityChange] < 0) OR ([Type] IN (3, 4) AND [QuantityChange] > 0)");
                table.HasCheckConstraint(
                    "CK_InventoryTransactions_OrderLink_MatchesType",
                    "([Type] IN (0, 1, 5) AND [OrderId] IS NULL) OR ([Type] IN (2, 3, 4) AND [OrderId] IS NOT NULL)");
            });

            builder.HasOne(transaction => transaction.Product)
                .WithMany(product => product.InventoryTransactions)
                .HasForeignKey(transaction => transaction.ProductId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(transaction => transaction.Order)
                .WithMany(order => order.InventoryTransactions)
                .HasForeignKey(transaction => transaction.OrderId)
                .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(transaction => transaction.CreatedByUser)
                .WithMany(user => user.InventoryTransactions)
                .HasForeignKey(transaction => transaction.CreatedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
