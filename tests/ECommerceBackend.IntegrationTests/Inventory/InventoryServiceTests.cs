using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public sealed class InventoryServiceTests
{
    [Fact]
    public async Task GetTransactionsAsync_ThrowsNotFoundWhenProductDoesNotExist()
    {
        await using var context = TestAppDbContext.Create();
        var service = new InventoryService(new InventoryRepository(context));

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetTransactionsAsync(
            Guid.NewGuid(),
            new InventoryQueryParams()));
    }

    [Fact]
    public async Task GetTransactionsAsync_ReturnsNewestTransactionsWithStablePaging()
    {
        await using var context = TestAppDbContext.Create();
        var (category, product) = CreateProduct(stockQuantity: 7);
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        context.AddRange(category, product);
        context.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = olderId,
                ProductId = product.Id,
                Product = product,
                Type = InventoryTransactionType.InitialStock,
                QuantityChange = 5,
                BalanceAfter = 5,
                CreatedAt = now.AddMinutes(-2)
            },
            new InventoryTransaction
            {
                Id = newerId,
                ProductId = product.Id,
                Product = product,
                Type = InventoryTransactionType.ManualAdjustment,
                QuantityChange = 2,
                BalanceAfter = 7,
                CreatedAt = now.AddMinutes(-1)
            });
        await context.SaveChangesAsync();

        var service = new InventoryService(new InventoryRepository(context));
        var result = await service.GetTransactionsAsync(product.Id, new InventoryQueryParams
        {
            Page = 1,
            PageSize = 1
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        var item = Assert.Single(result.Items);
        Assert.Equal(newerId, item.Id);
        Assert.Equal(product.Name, item.ProductName);
        Assert.Equal(nameof(InventoryTransactionType.ManualAdjustment), item.Type);
        Assert.Equal(5, item.BeforeQuantity);
        Assert.Equal(7, item.BalanceAfter);
    }

    [Fact]
    public async Task GetTransactionsAsync_FiltersHistoryAndProjectsTraceabilityFields()
    {
        await using var context = TestAppDbContext.Create();
        var (category, product) = CreateProduct(stockQuantity: 12);
        var actorUserId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);
        context.AddRange(category, product);
        context.InventoryTransactions.AddRange(
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Product = product,
                Type = InventoryTransactionType.ManualAdjustment,
                QuantityChange = -2,
                BalanceAfter = 8,
                CreatedByUserId = actorUserId,
                Reference = "ADJ-OLD",
                Reason = "Old adjustment",
                CreatedAt = now.AddDays(-2)
            },
            new InventoryTransaction
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Product = product,
                Type = InventoryTransactionType.StockIn,
                QuantityChange = 4,
                BalanceAfter = 12,
                CreatedByUserId = actorUserId,
                Reference = "GRN-NEW",
                Reason = "New stock",
                CreatedAt = now.AddHours(-1)
            });
        await context.SaveChangesAsync();

        var result = await new InventoryService(new InventoryRepository(context))
            .GetTransactionsAsync(product.Id, new InventoryQueryParams
            {
                Type = nameof(InventoryTransactionType.StockIn),
                From = now.AddDays(-1),
                To = now.AddHours(1),
                ActorUserId = actorUserId,
                Page = 1,
                PageSize = 10
            });

        var item = Assert.Single(result.Items);
        Assert.Equal(8, item.BeforeQuantity);
        Assert.Equal(4, item.QuantityChange);
        Assert.Equal(12, item.BalanceAfter);
        Assert.Equal("GRN-NEW", item.Reference);
        Assert.Equal("New stock", item.Reason);
    }

    [Fact]
    public async Task GetProductsAsync_AppliesLowStockFilterAndStableStockSort()
    {
        await using var context = TestAppDbContext.Create();
        var (category, lowStockProduct) = CreateProduct(stockQuantity: 2);
        var (_, availableProduct) = CreateProduct(stockQuantity: 8);
        availableProduct.CategoryId = category.Id;
        availableProduct.Category = category;
        context.AddRange(category, lowStockProduct, availableProduct);
        await context.SaveChangesAsync();

        var result = await new InventoryService(new InventoryRepository(context))
            .GetProductsAsync(new InventoryProductQueryParams
            {
                LowStockOnly = true,
                LowStockThreshold = 3,
                SortBy = "stock",
                SortOrder = "asc",
                Page = 1,
                PageSize = 10
            });

        var item = Assert.Single(result.Items);
        Assert.Equal(lowStockProduct.Id, item.ProductId);
        Assert.Equal(category.Name, item.CategoryName);
        Assert.Equal(2, item.StockQuantity);
    }

    [Fact]
    public async Task GetProductsAsync_UsesProductThresholdUnlessQueryOverridesIt()
    {
        await using var context = TestAppDbContext.Create();
        var (category, productThresholdMatch) = CreateProduct(stockQuantity: 7);
        _ = productThresholdMatch.SetLowStockThreshold(8);
        var (_, productThresholdMiss) = CreateProduct(stockQuantity: 4);
        productThresholdMiss.CategoryId = category.Id;
        productThresholdMiss.Category = category;
        _ = productThresholdMiss.SetLowStockThreshold(3);
        context.AddRange(category, productThresholdMatch, productThresholdMiss);
        await context.SaveChangesAsync();

        var service = new InventoryService(new InventoryRepository(context));
        var configured = await service.GetProductsAsync(new InventoryProductQueryParams
        {
            LowStockOnly = true,
            Page = 1,
            PageSize = 10
        });
        var configuredItem = Assert.Single(configured.Items);
        Assert.Equal(productThresholdMatch.Id, configuredItem.ProductId);
        Assert.Equal(8, configuredItem.LowStockThreshold);
        Assert.True(configuredItem.IsLowStock);

        var overridden = await service.GetProductsAsync(new InventoryProductQueryParams
        {
            LowStockOnly = true,
            LowStockThreshold = 5,
            Page = 1,
            PageSize = 10
        });
        Assert.Equal(
            new[] { productThresholdMiss.Id },
            overridden.Items.Select(item => item.ProductId));
    }

    [Fact]
    public async Task GetLowStockAsync_ExcludesDeletedProductsAndDeletedCategories()
    {
        await using var context = TestAppDbContext.Create();
        var (activeCategory, lowStockProduct) = CreateProduct(stockQuantity: 2);
        var (deletedCategory, hiddenByCategory) = CreateProduct(stockQuantity: 1);
        deletedCategory.IsDeleted = true;
        var (_, deletedProduct) = CreateProduct(stockQuantity: 0);
        deletedProduct.IsDeleted = true;
        context.AddRange(
            activeCategory,
            lowStockProduct,
            deletedCategory,
            hiddenByCategory,
            deletedProduct.Category!,
            deletedProduct);
        await context.SaveChangesAsync();

        var service = new InventoryService(new InventoryRepository(context));
        var result = await service.GetLowStockAsync(new LowStockQueryParams
        {
            Threshold = 2,
            Page = 1,
            PageSize = 10
        });

        var item = Assert.Single(result.Items);
        Assert.Equal(lowStockProduct.Id, item.ProductId);
        Assert.Equal(activeCategory.Name, item.CategoryName);
        Assert.Equal(2, item.StockQuantity);
    }

    private static (Category Category, Product Product) CreateProduct(int stockQuantity)
    {
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Category {Guid.NewGuid():N}",
            NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant()
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Category = category,
            Name = $"Product {Guid.NewGuid():N}",
            Price = 10,
            StockQuantity = stockQuantity,
            Description = "Inventory service test",
            CreatedAt = DateTime.UtcNow
        };

        return (category, product);
    }
}
