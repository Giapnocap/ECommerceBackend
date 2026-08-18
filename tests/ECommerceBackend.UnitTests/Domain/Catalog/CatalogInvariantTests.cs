using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Tests;

public sealed class CatalogInvariantTests
{
    [Fact]
    public void Product_UpdateDetailsAndAdjustStock_KeepSeparateInvariants()
    {
        var categoryId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 7, 28, 8, 0, 0, DateTimeKind.Utc);
        var product = Product.Create(
            Guid.NewGuid(),
            categoryId,
            "  Product  ",
            10.50m,
            5,
            "  Description  ",
            createdAt);

        product.UpdateDetails(
            categoryId,
            "  Updated product  ",
            12m,
            "  Updated description  ");
        var mutation = product.AdjustStockTo(2);

        Assert.Equal("Updated product", product.Name);
        Assert.Equal("Updated description", product.Description);
        Assert.Equal(12m, product.Price);
        Assert.Equal(2, product.StockQuantity);
        Assert.Equal(createdAt, product.CreatedAt);
        Assert.Equal(-3, mutation.QuantityChange);
        Assert.Equal(2, mutation.BalanceAfter);
    }

    [Fact]
    public void Product_InvalidUpdate_DoesNotPartiallyMutateAggregate()
    {
        var categoryId = Guid.NewGuid();
        var product = Product.Create(
            Guid.NewGuid(),
            categoryId,
            "Product",
            10m,
            5,
            "Description",
            DateTime.UtcNow);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            product.UpdateDetails(
                Guid.NewGuid(),
                "Changed",
                0,
                "Changed description"));

        Assert.Equal("product_price_invalid", exception.Code);
        Assert.Equal(categoryId, product.CategoryId);
        Assert.Equal("Product", product.Name);
        Assert.Equal(10m, product.Price);
        Assert.Equal(5, product.StockQuantity);
        Assert.Equal("Description", product.Description);
    }

    [Fact]
    public void Product_SoftDelete_IsOneWayAndIdempotent()
    {
        var product = Product.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Product",
            10m,
            1,
            string.Empty,
            DateTime.UtcNow);

        Assert.True(product.MarkDeleted());
        Assert.False(product.MarkDeleted());
        Assert.True(product.IsDeleted);
    }

    [Fact]
    public void Product_LowStockThreshold_IsBoundedAndIdempotent()
    {
        var product = Product.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Product",
            10m,
            1,
            string.Empty,
            DateTime.UtcNow);

        Assert.Equal(10, product.LowStockThreshold);
        Assert.True(product.SetLowStockThreshold(5));
        Assert.False(product.SetLowStockThreshold(5));
        Assert.Equal(5, product.LowStockThreshold);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            product.SetLowStockThreshold(-1));
        Assert.Equal("product_low_stock_threshold_invalid", exception.Code);
        Assert.Equal(5, product.LowStockThreshold);
    }

    [Fact]
    public void Category_HierarchyRulesRejectSelfChildAndThirdLevel()
    {
        var parent = Category.Create(Guid.NewGuid(), "Parent", null);
        var child = Category.Create(Guid.NewGuid(), "Child", parent);
        parent.Children.Add(child);

        var selfException = Assert.Throws<DomainRuleViolationException>(() =>
            parent.UpdateDetails("Parent", parent));
        var childException = Assert.Throws<DomainRuleViolationException>(() =>
            parent.UpdateDetails("Parent", child));
        var depthException = Assert.Throws<DomainRuleViolationException>(() =>
            Category.Create(Guid.NewGuid(), "Grandchild", child));

        Assert.Equal("business_error", selfException.Code);
        Assert.Equal("business_error", childException.Code);
        Assert.Equal("business_error", depthException.Code);
        Assert.Null(parent.ParentId);
    }

    [Fact]
    public void Category_DeleteRequiresNoActiveChildrenOrProducts()
    {
        var category = Category.Create(Guid.NewGuid(), "Category", null);
        var child = Category.Create(Guid.NewGuid(), "Child", category);
        category.Children.Add(child);

        Assert.Throws<DomainRuleViolationException>(() =>
            category.MarkDeleted());
        Assert.False(category.IsDeleted);

        category.Children.Clear();
        category.Products.Add(Product.Create(
            Guid.NewGuid(),
            category.Id,
            "Product",
            10m,
            1,
            string.Empty,
            DateTime.UtcNow));

        Assert.Throws<DomainRuleViolationException>(() =>
            category.MarkDeleted());
        Assert.False(category.IsDeleted);

        category.Products.Clear();
        Assert.True(category.MarkDeleted());
    }

    [Fact]
    public void CatalogInvariantSetters_AreNotPublic()
    {
        AssertSetterIsNotPublic<Product>(nameof(Product.CategoryId));
        AssertSetterIsNotPublic<Product>(nameof(Product.Price));
        AssertSetterIsNotPublic<Product>(nameof(Product.StockQuantity));
        AssertSetterIsNotPublic<Product>(nameof(Product.LowStockThreshold));
        AssertSetterIsNotPublic<Product>(nameof(Product.IsDeleted));
        AssertSetterIsNotPublic<Category>(nameof(Category.NormalizedName));
        AssertSetterIsNotPublic<Category>(nameof(Category.ParentId));
        AssertSetterIsNotPublic<Category>(nameof(Category.IsDeleted));
    }

    private static void AssertSetterIsNotPublic<TEntity>(string propertyName)
        => Assert.False(
            typeof(TEntity).GetProperty(propertyName)!.SetMethod!.IsPublic);
}
