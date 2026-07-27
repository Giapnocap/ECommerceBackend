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
        Assert.Equal(7, item.BalanceAfter);
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
