using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class CartConfiguration : IEntityTypeConfiguration<Cart>
    {
        public void Configure(EntityTypeBuilder<Cart> builder)
        {
            builder.HasIndex(cart => cart.UserId).IsUnique();
            builder.HasOne(cart => cart.User)
                .WithOne(user => user.Cart)
                .HasForeignKey<Cart>(cart => cart.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    internal sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
    {
        public void Configure(EntityTypeBuilder<CartItem> builder)
        {
            builder.Property(item => item.UnitPrice).HasPrecision(18, 2);
            builder.HasIndex(item => new { item.CartId, item.ProductId })
                .HasDatabaseName("UX_CartItems_CartId_ProductId")
                .IsUnique();
            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
                table.HasCheckConstraint("CK_CartItems_UnitPrice_Positive", "[UnitPrice] > 0");
            });

            builder.HasOne(item => item.Cart)
                .WithMany(cart => cart.CartItems)
                .HasForeignKey(item => item.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(item => item.Product)
                .WithMany(product => product.CartItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
