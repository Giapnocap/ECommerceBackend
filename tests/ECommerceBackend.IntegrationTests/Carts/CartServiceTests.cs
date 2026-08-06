using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class CartServiceTests
{
    [Fact]
    public async Task GetCartAsync_WhenCartDoesNotExist_ReturnsEmptyCartWithoutPersisting()
    {
        await using var context = TestAppDbContext.Create();
        var userId = Guid.NewGuid();
        var service = TestServiceFactory.CreateCartService(context);

        var first = await service.GetCartAsync(userId);
        var second = await service.GetCartAsync(userId);

        Assert.Equal(Guid.Empty, first.Id);
        Assert.Empty(first.Items);
        Assert.Equal(Guid.Empty, second.Id);
        Assert.Empty(second.Items);
        Assert.False(await context.Carts.AnyAsync(cart => cart.UserId == userId));
        Assert.Empty(context.ChangeTracker.Entries<Cart>());
    }

    [Fact]
    public async Task CartLifecycle_AddsMergesUpdatesRemovesAndClearsItems()
    {
        await using var context = TestAppDbContext.Create();
        var product = await SeedProductAsync(context, stockQuantity: 10);
        var secondProduct = await SeedProductAsync(context, stockQuantity: 5);
        var userId = Guid.NewGuid();
        var service = TestServiceFactory.CreateCartService(context);

        var empty = await service.GetCartAsync(userId);
        Assert.Empty(empty.Items);
        Assert.False(await context.Carts.AnyAsync(cart => cart.UserId == userId));

        var added = await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 2
        });
        var merged = await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 1
        });
        var item = Assert.Single(merged.Items);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(product.Price * 3, merged.TotalAmount);

        var updated = await service.UpdateItemAsync(userId, item.Id, new UpdateCartItemRequest
        {
            Quantity = 4
        });
        Assert.Equal(4, Assert.Single(updated.Items).Quantity);

        Assert.Empty((await service.UpdateItemAsync(userId, item.Id, new UpdateCartItemRequest
        {
            Quantity = 0
        })).Items);
        var readded = await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 1
        });
        Assert.Empty((await service.RemoveItemAsync(
            userId,
            Assert.Single(readded.Items).Id)).Items);
        Assert.Single((await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = secondProduct.Id,
            Quantity = 1
        })).Items);

        await service.ClearCartAsync(userId);
        Assert.Empty((await service.GetCartAsync(userId)).Items);
        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.Equal(1, await context.Carts.CountAsync(cart => cart.UserId == userId));
    }

    [Fact]
    public async Task AddItemAsync_RejectsMissingProductAndInsufficientStock()
    {
        await using var context = TestAppDbContext.Create();
        var product = await SeedProductAsync(context, stockQuantity: 2);
        var service = TestServiceFactory.CreateCartService(context);

        await Assert.ThrowsAsync<NotFoundException>(() => service.AddItemAsync(
            Guid.NewGuid(),
            new AddToCartRequest { ProductId = Guid.NewGuid(), Quantity = 1 }));
        await Assert.ThrowsAsync<BusinessException>(() => service.AddItemAsync(
            Guid.NewGuid(),
            new AddToCartRequest { ProductId = product.Id, Quantity = 3 }));
    }

    [Fact]
    public async Task UpdateAndRemove_RejectUnknownCartItem()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateCartService(context);
        var userId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateItemAsync(
            userId,
            Guid.NewGuid(),
            new UpdateCartItemRequest { Quantity = 1 }));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveItemAsync(
            userId,
            Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateItemAsync_RejectsProductThatStoppedSelling()
    {
        await using var context = TestAppDbContext.Create();
        var product = await SeedProductAsync(context, stockQuantity: 2);
        var service = TestServiceFactory.CreateCartService(context);
        var userId = Guid.NewGuid();
        var cart = await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 1
        });
        var storedProduct = await context.Products.SingleAsync(candidate => candidate.Id == product.Id);
        storedProduct.IsDeleted = true;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessException>(() => service.UpdateItemAsync(
            userId,
            Assert.Single(cart.Items).Id,
            new UpdateCartItemRequest { Quantity = 2 }));
    }

    [Fact]
    public async Task Mutations_RejectInvalidQuantityBeforeChangingCart()
    {
        await using var context = TestAppDbContext.Create();
        var product = await SeedProductAsync(context, stockQuantity: 5);
        var service = TestServiceFactory.CreateCartService(context);
        var userId = Guid.NewGuid();

        var addException = await Assert.ThrowsAsync<BusinessException>(() => service.AddItemAsync(
            userId,
            new AddToCartRequest { ProductId = product.Id, Quantity = -1 }));
        Assert.Equal("cart_quantity_invalid", addException.Code);
        Assert.Empty(context.CartItems);

        var cart = await service.AddItemAsync(userId, new AddToCartRequest
        {
            ProductId = product.Id,
            Quantity = 1
        });
        var itemId = Assert.Single(cart.Items).Id;
        var updateException = await Assert.ThrowsAsync<BusinessException>(() => service.UpdateItemAsync(
            userId,
            itemId,
            new UpdateCartItemRequest { Quantity = -1 }));

        Assert.Equal("cart_quantity_invalid", updateException.Code);
        Assert.Equal(1, (await context.CartItems.AsNoTracking().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task AddItemAsync_AtLineLimit_MergesExistingButRejectsNewProduct()
    {
        await using var context = TestAppDbContext.Create();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"cart_limit_{Guid.NewGuid():N}"[..20],
            NormalizedUserName = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Email = $"{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM",
            FullName = "Cart Limit Customer",
            PasswordHash = "test-hash"
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Category {Guid.NewGuid():N}",
            NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant()
        };
        var cart = Cart.Create(Guid.NewGuid(), user.Id);
        var products = Enumerable.Range(0, Cart.MaximumLineItems + 1)
            .Select(index => Product.Create(
                Guid.NewGuid(),
                category.Id,
                $"Product {index}",
                25m,
                5,
                "Cart limit test",
                DateTime.UtcNow))
            .ToArray();
        var items = products
            .Take(Cart.MaximumLineItems)
            .Select(product => new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = 1,
                UnitPrice = product.Price
            })
            .ToArray();
        context.AddRange(user, category, cart);
        context.Products.AddRange(products);
        context.CartItems.AddRange(items);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var service = TestServiceFactory.CreateCartService(context);

        var merged = await service.AddItemAsync(user.Id, new AddToCartRequest
        {
            ProductId = products[0].Id,
            Quantity = 1
        });
        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.AddItemAsync(user.Id, new AddToCartRequest
            {
                ProductId = products[^1].Id,
                Quantity = 1
            }));

        Assert.Equal(
            2,
            merged.Items.Single(item => item.ProductId == products[0].Id).Quantity);
        Assert.Equal("cart_line_item_limit_exceeded", exception.Code);
        Assert.Equal(Cart.MaximumLineItems, await context.CartItems.CountAsync());
        Assert.DoesNotContain(
            await context.CartItems.AsNoTracking().ToListAsync(),
            item => item.ProductId == products[^1].Id);
    }

    private static async Task<Product> SeedProductAsync(
        AppDbContext context,
        int stockQuantity)
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
            Price = 25,
            StockQuantity = stockQuantity,
            Description = "Cart service test",
            CreatedAt = DateTime.UtcNow
        };
        context.AddRange(category, product);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return product;
    }
}
