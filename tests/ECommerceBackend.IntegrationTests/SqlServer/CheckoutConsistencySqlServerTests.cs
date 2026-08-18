using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class CheckoutConsistencySqlServerTests
{
    private static readonly DateTimeOffset Now = new(
        2026,
        8,
        7,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentCustomers_CompetingForLastItem_CreateOneOrder()
    {
        SqlServerIntegrationTestGate.Require();
        var options = CreateOptions(
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}");

        try
        {
            var fixture = await SeedCheckoutAsync(
                options,
                stockQuantity: 1,
                customerCount: 2);
            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<CheckoutOutcome> CheckoutAsync(
                CheckoutCustomer customer,
                string idempotencyKey)
            {
                await start.Task;
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(Now));
                try
                {
                    var response = await service.PlaceOrderAsync(
                        customer.UserId,
                        CreateRequest(),
                        idempotencyKey);
                    return new CheckoutOutcome(response, null);
                }
                catch (Exception ex)
                {
                    return new CheckoutOutcome(null, ex);
                }
            }

            var attempts = fixture.Customers
                .Select((customer, index) => CheckoutAsync(
                    customer,
                    $"last-stock-{index}"))
                .ToArray();
            start.SetResult(true);
            var outcomes = await Task.WhenAll(attempts);

            Assert.Single(outcomes, outcome => outcome.Response != null);
            var failure = Assert.Single(
                outcomes,
                outcome => outcome.Exception != null);
            var businessError = Assert.IsType<BusinessException>(
                failure.Exception);
            Assert.Equal("inventory_insufficient", businessError.Code);

            await using var verificationContext =
                new AppDbContext(options);
            Assert.Equal(
                0,
                await verificationContext.Products
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.StockQuantity)
                    .SingleAsync());
            Assert.Equal(1, await verificationContext.Orders.CountAsync());
            Assert.Equal(1, await verificationContext.Payments.CountAsync());
            Assert.Equal(1, await verificationContext.CartItems.CountAsync());
            Assert.Equal(
                1,
                await verificationContext.InventoryTransactions.CountAsync(
                    transaction => transaction.Type
                        == InventoryTransactionType.OrderPlaced));
            Assert.Equal(1, await verificationContext.OutboxMessages.CountAsync());
        }
        finally
        {
            await DeleteDatabaseAsync(options);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentStockInAndCheckout_PreserveInventoryAndLedgerConsistency()
    {
        SqlServerIntegrationTestGate.Require();
        var options = CreateOptions(
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}");

        try
        {
            var fixture = await SeedCheckoutAsync(
                options,
                stockQuantity: 1,
                customerCount: 1);
            byte[] initialVersion;
            await using (var versionContext = new AppDbContext(options))
            {
                initialVersion = await versionContext.Products
                    .AsNoTracking()
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.RowVersion)
                    .SingleAsync();
            }

            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var customer = Assert.Single(fixture.Customers);

            async Task<CheckoutOutcome> CheckoutAsync()
            {
                await start.Task;
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(Now));
                try
                {
                    var response = await service.PlaceOrderAsync(
                        customer.UserId,
                        CreateRequest(),
                        "checkout-during-stock-in");
                    return new CheckoutOutcome(response, null);
                }
                catch (Exception ex)
                {
                    return new CheckoutOutcome(null, ex);
                }
            }

            async Task<Exception?> StockInAsync(byte[] expectedVersion)
            {
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateProductService(
                    context,
                    new FixedTimeProvider(Now));
                try
                {
                    await service.StockInAsync(
                        fixture.ProductId,
                        new StockInRequest
                        {
                            Quantity = 5,
                            Reference = "CONCURRENT-STOCK-IN",
                            Reason = "Concurrent stock-in test"
                        },
                        expectedVersion);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            async Task<Exception?> StockInWithRetryAsync()
            {
                await start.Task;
                var firstAttempt = await StockInAsync(initialVersion);
                if (firstAttempt is not ConflictException)
                    return firstAttempt;

                await using var versionContext = new AppDbContext(options);
                var refreshedVersion = await versionContext.Products
                    .AsNoTracking()
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.RowVersion)
                    .SingleAsync();
                return await StockInAsync(refreshedVersion);
            }

            var checkout = CheckoutAsync();
            var stockIn = StockInWithRetryAsync();
            start.SetResult(true);
            var checkoutOutcome = await checkout;
            var stockInException = await stockIn;

            Assert.Null(checkoutOutcome.Exception);
            Assert.NotNull(checkoutOutcome.Response);
            Assert.Null(stockInException);

            await using var verificationContext = new AppDbContext(options);
            Assert.Equal(
                5,
                await verificationContext.Products
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.StockQuantity)
                    .SingleAsync());
            var mutations = await verificationContext.InventoryTransactions
                .Where(transaction => transaction.ProductId == fixture.ProductId)
                .ToListAsync();
            var placed = Assert.Single(
                mutations,
                transaction => transaction.Type == InventoryTransactionType.OrderPlaced);
            var stockInTransaction = Assert.Single(
                mutations,
                transaction => transaction.Type == InventoryTransactionType.StockIn);

            Assert.Equal(-1, placed.QuantityChange);
            Assert.Equal(5, stockInTransaction.QuantityChange);
            Assert.Equal("CONCURRENT-STOCK-IN", stockInTransaction.Reference);

            var stockBeforeCheckout = placed.BalanceAfter - placed.QuantityChange;
            var stockBeforeStockIn = stockInTransaction.BalanceAfter - stockInTransaction.QuantityChange;
            var checkoutCommittedFirst =
                stockBeforeCheckout == 1
                && placed.BalanceAfter == 0
                && stockBeforeStockIn == 0
                && stockInTransaction.BalanceAfter == 5;
            var stockInCommittedFirst =
                stockBeforeStockIn == 1
                && stockInTransaction.BalanceAfter == 6
                && stockBeforeCheckout == 6
                && placed.BalanceAfter == 5;

            Assert.True(
                checkoutCommittedFirst || stockInCommittedFirst,
                "Concurrent checkout and stock-in must produce one serial ledger sequence without a lost update.");
        }
        finally
        {
            await DeleteDatabaseAsync(options);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentDuplicateCheckout_ReturnsOneCommittedOrder()
    {
        SqlServerIntegrationTestGate.Require();
        var options = CreateOptions(
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}");

        try
        {
            var fixture = await SeedCheckoutAsync(
                options,
                stockQuantity: 2,
                customerCount: 1);
            var customer = Assert.Single(fixture.Customers);
            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<CheckoutOutcome> CheckoutAsync()
            {
                await start.Task;
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(Now));
                try
                {
                    var response = await service.PlaceOrderAsync(
                        customer.UserId,
                        CreateRequest(),
                        "concurrent-idempotency-key");
                    return new CheckoutOutcome(response, null);
                }
                catch (Exception ex)
                {
                    return new CheckoutOutcome(null, ex);
                }
            }

            var attempts = new[] { CheckoutAsync(), CheckoutAsync() };
            start.SetResult(true);
            var outcomes = await Task.WhenAll(attempts);

            Assert.All(outcomes, outcome => Assert.Null(outcome.Exception));
            Assert.Equal(
                outcomes[0].Response!.Id,
                outcomes[1].Response!.Id);

            await using var verificationContext =
                new AppDbContext(options);
            Assert.Equal(
                1,
                await verificationContext.Products
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.StockQuantity)
                    .SingleAsync());
            Assert.Equal(1, await verificationContext.Orders.CountAsync());
            Assert.Equal(1, await verificationContext.Payments.CountAsync());
            Assert.Empty(await verificationContext.CartItems.ToListAsync());
            Assert.Equal(
                1,
                await verificationContext.InventoryTransactions.CountAsync(
                    transaction => transaction.Type
                        == InventoryTransactionType.OrderPlaced));
            Assert.Equal(1, await verificationContext.OutboxMessages.CountAsync());
        }
        finally
        {
            await DeleteDatabaseAsync(options);
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task OutboxWriteFailure_RollsBackOrderInventoryAndCart()
    {
        SqlServerIntegrationTestGate.Require();
        var options = CreateOptions(
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}");

        try
        {
            var fixture = await SeedCheckoutAsync(
                options,
                stockQuantity: 2,
                customerCount: 1);
            var customer = Assert.Single(fixture.Customers);

            await using (var context = new AppDbContext(options))
            {
                await context.Database.ExecuteSqlRawAsync(
                    """
                    CREATE TRIGGER [TR_OutboxMessages_RejectCheckoutTest]
                    ON [dbo].[OutboxMessages]
                    INSTEAD OF INSERT
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        THROW 51099, 'Forced outbox persistence failure.', 1;
                    END;
                    """);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(Now));

                var exception = await Assert.ThrowsAsync<DbUpdateException>(
                    () => service.PlaceOrderAsync(
                        customer.UserId,
                        CreateRequest(),
                        "rollback-on-outbox-failure"));

                Assert.Equal(51099, FindSqlException(exception)?.Number);
            }

            await using var verificationContext =
                new AppDbContext(options);
            Assert.Equal(
                2,
                await verificationContext.Products
                    .Where(product => product.Id == fixture.ProductId)
                    .Select(product => product.StockQuantity)
                    .SingleAsync());
            Assert.Equal(1, await verificationContext.CartItems.CountAsync());
            Assert.Empty(await verificationContext.Orders.ToListAsync());
            Assert.Empty(await verificationContext.Payments.ToListAsync());
            Assert.Empty(
                await verificationContext.InventoryTransactions.ToListAsync());
            Assert.Empty(await verificationContext.OutboxMessages.ToListAsync());
        }
        finally
        {
            await DeleteDatabaseAsync(options);
        }
    }

    private static async Task<CheckoutFixture> SeedCheckoutAsync(
        DbContextOptions<AppDbContext> options,
        int stockQuantity,
        int customerCount)
    {
        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Checkout consistency",
            NormalizedName = "CHECKOUT CONSISTENCY"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = "Checkout consistency product",
            Price = 250_000m,
            StockQuantity = stockQuantity,
            Description = "SQL Server checkout consistency product",
            CreatedAt = Now.UtcDateTime
        };
        var customers = new List<CheckoutCustomer>(customerCount);

        context.AddRange(category, product);
        for (var index = 0; index < customerCount; index++)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = $"checkout_consistency_{index}",
                NormalizedUserName = $"CHECKOUT_CONSISTENCY_{index}",
                Email = $"checkout_consistency_{index}@example.com",
                NormalizedEmail =
                    $"CHECKOUT_CONSISTENCY_{index}@EXAMPLE.COM",
                FullName = $"Checkout Customer {index}",
                Phone = $"09000000{index:00}",
                PasswordHash = "not-used",
                CreatedAt = Now.UtcDateTime
            };
            var cart = new Cart
            {
                Id = Guid.NewGuid(),
                UserId = user.Id
            };
            context.AddRange(
                user,
                cart,
                new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = cart.Id,
                    ProductId = product.Id,
                    Quantity = 1,
                    UnitPrice = product.Price
                });
            customers.Add(new CheckoutCustomer(user.Id));
        }

        await context.SaveChangesAsync();
        return new CheckoutFixture(product.Id, customers);
    }

    private static PlaceOrderRequest CreateRequest()
        => new()
        {
            ShippingAddress = "1 Checkout Consistency Street",
            PaymentMethod = PaymentMethod.CashOnDelivery
        };

    private static DbContextOptions<AppDbContext> CreateOptions(
        string databaseName)
        => new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                SqlServerIntegrationTestGate
                    .CreateTestDatabaseConnectionString(databaseName))
            .Options;

    private static async Task DeleteDatabaseAsync(
        DbContextOptions<AppDbContext> options)
    {
        await using var context = new AppDbContext(options);
        await context.Database.EnsureDeletedAsync();
    }

    private static SqlException? FindSqlException(Exception exception)
        => exception switch
        {
            SqlException sqlException => sqlException,
            { InnerException: not null } =>
                FindSqlException(exception.InnerException),
            _ => null
        };

    private sealed record CheckoutCustomer(Guid UserId);

    private sealed record CheckoutFixture(
        Guid ProductId,
        IReadOnlyList<CheckoutCustomer> Customers);

    private sealed record CheckoutOutcome(
        OrderResponse? Response,
        Exception? Exception);
}
