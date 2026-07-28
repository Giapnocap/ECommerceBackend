using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ECommerceBackend.Infrastructure.Data.Configurations
{
    internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.Property(category => category.RowVersion).IsRowVersion();
            builder.HasIndex(category => category.NormalizedName)
                .HasDatabaseName("UX_Categories_Root_NormalizedName")
                .HasFilter("[IsDeleted] = 0 AND [ParentId] IS NULL")
                .IsUnique();
            builder.HasIndex(category => new { category.ParentId, category.NormalizedName })
                .HasDatabaseName("UX_Categories_ParentId_NormalizedName")
                .HasFilter("[IsDeleted] = 0 AND [ParentId] IS NOT NULL")
                .IsUnique();
            builder.Property(category => category.Name).HasMaxLength(100);
            builder.Property(category => category.NormalizedName).HasMaxLength(100);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Categories_Parent_NotSelf",
                    "[ParentId] IS NULL OR [ParentId] <> [Id]");
            });

            builder.HasOne(category => category.Parent)
                .WithMany(category => category.Children)
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.Property(product => product.Price).HasPrecision(18, 2);
            builder.Property(product => product.RowVersion).IsRowVersion();
            builder.HasIndex(product => new { product.IsDeleted, product.StockQuantity });
            builder.HasIndex(product => new { product.IsDeleted, product.CreatedAt, product.Id })
                .HasDatabaseName("IX_Products_IsDeleted_CreatedAt_Id")
                .IsDescending(false, true, true);
            builder.Property(product => product.Name).HasMaxLength(200);
            builder.Property(product => product.Description).HasMaxLength(2000);
            builder.ToTable(table =>
            {
                table.HasCheckConstraint("CK_Products_Price_Positive", "[Price] > 0");
                table.HasCheckConstraint("CK_Products_Stock_NonNegative", "[StockQuantity] >= 0");
            });

            builder.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.NoAction);
        }
    }

    internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
    {
        public void Configure(EntityTypeBuilder<ProductImage> builder)
        {
            builder.HasIndex(image => image.ProductId)
                .HasDatabaseName("UX_ProductImages_ProductId_IsMain")
                .HasFilter("[IsMain] = 1")
                .IsUnique();
            builder.Property(image => image.ImageUrl).HasMaxLength(500);

            builder.HasOne(image => image.Product)
                .WithMany(product => product.Images)
                .HasForeignKey(image => image.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
