using System.Diagnostics.Metrics;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public class ProductServiceTests
{
    [Fact]
    public async Task CreateAsync_WithActiveCategory_TrimsAndPersistsProduct()
    {
        await using var context = TestAppDbContext.Create();
        var category = new Category { Id = Guid.NewGuid(), Name = "Phones" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var service = TestServiceFactory.CreateProductService(context);

        var response = await service.CreateAsync(new CreateProductRequest
        {
            Name = "  Test Phone  ",
            Price = 1000.50m,
            StockQuantity = 5,
            Description = "  Flagship phone  ",
            CategoryId = category.Id
        });

        var product = await context.Products.SingleAsync(product => product.Id == response.Id);
        Assert.Equal("Test Phone", response.Name);
        Assert.Equal("Phones", response.CategoryName);
        Assert.Equal("Test Phone", product.Name);
        Assert.Equal("Flagship phone", product.Description);
        Assert.Contains(context.InventoryTransactions, transaction =>
            transaction.ProductId == product.Id
            && transaction.QuantityChange == 5
            && transaction.BalanceAfter == 5);
    }

    [Fact]
    public async Task UpdateAsync_WritesInventoryMutationWithInjectedTimestamp()
    {
        var now = new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero);
        var actorUserId = Guid.NewGuid();
        await using var context = TestAppDbContext.Create();
        var category = new Category { Id = Guid.NewGuid(), Name = "Inventory" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateProductService(
            context,
            new FixedTimeProvider(now));

        var created = await service.CreateAsync(new CreateProductRequest
        {
            Name = "Inventory Product",
            Price = 25m,
            StockQuantity = 5,
            Description = "Inventory product",
            CategoryId = category.Id
        }, actorUserId);
        await service.UpdateAsync(created.Id, new UpdateProductRequest
        {
            Name = "Inventory Product",
            Price = 25m,
            StockQuantity = 2,
            Description = "Inventory product",
            CategoryId = category.Id
        }, actorUserId);

        var product = await context.Products.SingleAsync(candidate => candidate.Id == created.Id);
        var ledger = await context.InventoryTransactions
            .Where(transaction => transaction.ProductId == created.Id)
            .OrderBy(transaction => transaction.Type)
            .ToListAsync();
        Assert.Equal(now.UtcDateTime, product.CreatedAt);
        Assert.Equal(2, product.StockQuantity);
        Assert.Collection(
            ledger,
            initial =>
            {
                Assert.Equal(5, initial.QuantityChange);
                Assert.Equal(5, initial.BalanceAfter);
                Assert.Equal(now.UtcDateTime, initial.CreatedAt);
            },
            adjustment =>
            {
                Assert.Equal(-3, adjustment.QuantityChange);
                Assert.Equal(2, adjustment.BalanceAfter);
                Assert.Equal(now.UtcDateTime, adjustment.CreatedAt);
            });
    }

    [Fact]
    public async Task GetAllAsync_FiltersSortsAndPagesActiveProductsOnly()
    {
        await using var context = TestAppDbContext.Create();
        var category = new Category { Id = Guid.NewGuid(), Name = "Accessories" };
        var deletedCategory = new Category { Id = Guid.NewGuid(), Name = "Deleted", IsDeleted = true };
        context.Categories.AddRange(category, deletedCategory);

        context.Products.AddRange(
            Product("Cable", 20, category.Id),
            Product("Adapter", 15, category.Id),
            Product("Case", 10, category.Id, isDeleted: true),
            Product("Hidden", 99, deletedCategory.Id));
        await context.SaveChangesAsync();

        var service = TestServiceFactory.CreateProductService(context);

        var result = await service.GetAllAsync(new ProductQueryParams
        {
            CategoryId = category.Id,
            MinPrice = 10,
            MaxPrice = 25,
            SortBy = "price",
            SortOrder = "desc",
            Page = 1,
            PageSize = 10
        });

        var items = result.Items.ToArray();
        Assert.Equal(2, result.TotalCount);
        Assert.Collection(
            items,
            item => Assert.Equal("Cable", item.Name),
            item => Assert.Equal("Adapter", item.Name));
    }

    [Fact]
    public async Task GetAllAsync_EmitsBoundedMetricsWithoutSearchText()
    {
        const string searchText = "private-search-text";
        var observations = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "ECommerceBackend.Catalog")
                currentListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == "catalog.queries")
                observations.Add(tags.ToArray());
        });
        listener.Start();

        await using var context = TestAppDbContext.Create();
        var category = new Category { Id = Guid.NewGuid(), Name = "Metrics" };
        context.Categories.Add(category);
        context.Products.Add(Product(searchText, 10, category.Id));
        await context.SaveChangesAsync();

        var service = TestServiceFactory.CreateProductService(context);
        _ = await service.GetAllAsync(new ProductQueryParams
        {
            Keyword = searchText,
            Page = 1,
            PageSize = 10
        });

        var tags = Assert.Single(observations);
        Assert.Contains(tags, tag => tag.Key == "catalog.has_search" && Equals(tag.Value, true));
        Assert.DoesNotContain(tags, tag => tag.Value?.ToString()?.Contains(searchText) == true);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesProduct()
    {
        await using var context = TestAppDbContext.Create();
        var category = new Category { Id = Guid.NewGuid(), Name = "Books" };
        var product = Product("Clean Code", 30, category.Id);
        context.Categories.Add(category);
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var service = TestServiceFactory.CreateProductService(context);

        await service.DeleteAsync(product.Id);

        Assert.True(await context.Products.AnyAsync(p => p.Id == product.Id && p.IsDeleted));
        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(product.Id));
    }

    private static Product Product(string name, decimal price, Guid categoryId, bool isDeleted = false)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Price = price,
            StockQuantity = 10,
            Description = $"{name} description",
            CategoryId = categoryId,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = isDeleted
        };
}
