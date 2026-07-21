using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class OrderLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OrderExpirationAndCancellation_ProtectLifecycleMetadata()
    {
        var order = CreateOrderEntity();
        var expiresAt = order.OrderDate.AddMinutes(30);

        order.SetPendingExpiration(expiresAt);
        var change = order.Cancel(expiresAt, PaymentStatus.Pending, "SystemExpired", isExpiration: true);

        Assert.True(change.Changed);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(expiresAt, order.ExpiresAt);
        Assert.Equal(expiresAt, order.CancelledAt);
        Assert.Equal(expiresAt, order.ExpiredAt);
        Assert.Equal("SystemExpired", order.CancellationReason);
    }

    [Fact]
    public async Task PlaceOrder_SetsHoldAndEnforcesPendingOrderLimit()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context, stock: 10);
        var options = new OrderLifecycleOptions
        {
            PendingCodHoldMinutes = 30,
            MaxPendingOrdersPerCustomer = 3
        };
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now),
            options);

        Guid thirdOrderId = Guid.Empty;
        for (var index = 1; index <= 3; index++)
        {
            await AddCartItemAsync(context, fixture.Cart.Id, fixture.Product.Id);
            var order = await service.PlaceOrderAsync(
                fixture.User.Id,
                CreateRequest(),
                $"pending-limit-{index}");

            Assert.Equal(Now.UtcDateTime.AddMinutes(30), order.ExpiresAt);
            if (index == 3)
                thirdOrderId = order.Id;
        }

        var idempotentRetry = await service.PlaceOrderAsync(
            fixture.User.Id,
            CreateRequest(),
            "pending-limit-3");
        Assert.Equal(thirdOrderId, idempotentRetry.Id);

        await AddCartItemAsync(context, fixture.Cart.Id, fixture.Product.Id);
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.PlaceOrderAsync(fixture.User.Id, CreateRequest(), "pending-limit-4"));

        Assert.Equal("pending_order_limit_reached", exception.Code);
        Assert.Equal(3, await context.Orders.CountAsync(order => order.Status == OrderStatus.Pending));
        Assert.Single(await context.CartItems.ToListAsync());
    }

    [Fact]
    public async Task CustomerCancellation_IsIdempotentAndReleasesStockOnce()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context, stock: 2);
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));
        await AddCartItemAsync(context, fixture.Cart.Id, fixture.Product.Id);
        var placed = await service.PlaceOrderAsync(
            fixture.User.Id,
            CreateRequest(),
            "customer-cancel");

        var first = await service.CancelByCustomerAsync(
            placed.Id,
            fixture.User.Id,
            new CancelOrderRequest { Reason = "Changed my mind" });
        var retry = await service.CancelByCustomerAsync(
            placed.Id,
            fixture.User.Id,
            new CancelOrderRequest { Reason = "Changed my mind" });

        Assert.Equal(nameof(OrderStatus.Cancelled), first.Status);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal("Changed my mind", first.CancellationReason);
        Assert.Equal(2, await context.Products
            .Where(product => product.Id == fixture.Product.Id)
            .Select(product => product.StockQuantity)
            .SingleAsync());
        Assert.Equal(1, await context.InventoryTransactions.CountAsync(transaction =>
            transaction.OrderId == placed.Id
            && transaction.Type == InventoryTransactionType.OrderCancelled));
    }

    [Fact]
    public async Task Expiration_IsIdempotentAndReleasesStockOnce()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context, stock: 2);
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));
        await AddCartItemAsync(context, fixture.Cart.Id, fixture.Product.Id);
        var placed = await service.PlaceOrderAsync(
            fixture.User.Id,
            CreateRequest(),
            "expiration");
        var asOf = Now.UtcDateTime.AddMinutes(31);

        var dueIds = await service.GetDuePendingOrderIdsAsync(asOf, 10);
        var first = await service.ExpirePendingOrderAsync(placed.Id, asOf);
        var retry = await service.ExpirePendingOrderAsync(placed.Id, asOf);

        Assert.Contains(placed.Id, dueIds);
        Assert.True(first);
        Assert.False(retry);
        var expired = await context.Orders.AsNoTracking().SingleAsync(order => order.Id == placed.Id);
        Assert.Equal(OrderStatus.Cancelled, expired.Status);
        Assert.Equal(asOf, expired.ExpiredAt);
        Assert.Equal("SystemExpired", expired.CancellationReason);
        Assert.Equal(1, await context.InventoryTransactions.CountAsync(transaction =>
            transaction.OrderId == placed.Id
            && transaction.Type == InventoryTransactionType.OrderCancelled));
    }

    [Fact]
    public async Task Checkout_PreflightsEveryLineBeforeMutatingInventoryOrOrderState()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context, stock: 2);
        var unavailable = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = fixture.Product.CategoryId,
            Name = "Unavailable Product",
            Price = 50,
            StockQuantity = 1,
            IsDeleted = true,
            CreatedAt = Now.UtcDateTime
        };
        context.Products.Add(unavailable);
        await context.SaveChangesAsync();
        await AddCartItemAsync(context, fixture.Cart.Id, fixture.Product.Id);
        context.CartItems.Add(new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = fixture.Cart.Id,
            ProductId = unavailable.Id,
            Quantity = 1,
            UnitPrice = unavailable.Price
        });
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));

        var exception = await Assert.ThrowsAsync<BusinessException>(() => service.PlaceOrderAsync(
            fixture.User.Id,
            CreateRequest(),
            "unavailable-product"));

        Assert.Equal("inventory_product_unavailable", exception.Code);
        Assert.Equal(2, fixture.Product.StockQuantity);
        Assert.Equal(1, unavailable.StockQuantity);
        Assert.Equal(2, await context.CartItems.AsNoTracking().CountAsync());
        Assert.Empty(context.ChangeTracker.Entries<Order>());
        Assert.Empty(context.ChangeTracker.Entries<Payment>());
        Assert.Empty(await context.InventoryTransactions.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task GetAllOrdersAsync_FiltersByCustomerAndPaginatesNewestFirst()
    {
        await using var context = TestAppDbContext.Create();
        var customer = ListedUser("list_customer");
        var otherCustomer = ListedUser("list_other");
        context.Users.AddRange(customer, otherCustomer);
        var customerId = customer.Id;
        var otherCustomerId = otherCustomer.Id;
        var oldest = ListedOrder(customerId, "LIST-001", Now.UtcDateTime.AddMinutes(-3));
        var middle = ListedOrder(customerId, "LIST-002", Now.UtcDateTime.AddMinutes(-2));
        var newest = ListedOrder(customerId, "LIST-003", Now.UtcDateTime.AddMinutes(-1));
        context.Orders.AddRange(
            oldest,
            middle,
            newest,
            ListedOrder(otherCustomerId, "LIST-OTHER", Now.UtcDateTime));
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateOrderService(context, new FixedTimeProvider(Now));

        var result = await service.GetAllOrdersAsync(new OrderQueryParams
        {
            UserId = customerId,
            Status = OrderStatus.Pending,
            Page = 2,
            PageSize = 1
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.Equal(middle.Id, Assert.Single(result.Items).Id);
    }

    private static Order CreateOrderEntity()
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrderDate = Now.UtcDateTime,
            ShippingAddress = "Address"
        };
        order.SetPricing(100, 0, 0, 0);
        return order;
    }

    private static Order ListedOrder(Guid userId, string number, DateTime orderDate)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderNumber = number,
            IdempotencyKey = $"key-{number}",
            IdempotencyRequestHash = new string('A', 64),
            OrderDate = orderDate,
            ShippingAddress = "Listing test"
        };
        order.SetPricing(100, 0, 0, 0);
        return order;
    }

    private static User ListedUser(string userName)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FullName = userName,
            PasswordHash = "test-hash",
            CreatedAt = Now.UtcDateTime
        };

    private static PlaceOrderRequest CreateRequest() => new()
    {
        ShippingAddress = "1 Test Street",
        PaymentMethod = PaymentMethod.CashOnDelivery
    };

    private static async Task<(User User, Cart Cart, Product Product)> SeedCheckoutAsync(
        Infrastructure.Data.AppDbContext context,
        int stock)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"customer_{Guid.NewGuid():N}"[..20],
            NormalizedUserName = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Email = $"{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM",
            FullName = "Lifecycle Customer",
            PasswordHash = "hash",
            CreatedAt = Now.UtcDateTime
        };
        var cart = new Cart { Id = Guid.NewGuid(), UserId = user.Id };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Lifecycle",
            NormalizedName = $"LIFECYCLE_{Guid.NewGuid():N}"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = "Lifecycle Product",
            Price = 100,
            StockQuantity = stock,
            CreatedAt = Now.UtcDateTime
        };
        context.AddRange(user, cart, category, product);
        await context.SaveChangesAsync();
        return (user, cart, product);
    }

    private static async Task AddCartItemAsync(
        Infrastructure.Data.AppDbContext context,
        Guid cartId,
        Guid productId)
    {
        context.CartItems.Add(new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cartId,
            ProductId = productId,
            Quantity = 1,
            UnitPrice = 100
        });
        await context.SaveChangesAsync();
    }
}
