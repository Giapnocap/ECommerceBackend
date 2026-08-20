using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.Tests;

[Collection(SqlServerPerformanceTestCollection.Name)]
public sealed class SqlServerPerformanceTests
{
    private const int CatalogProductCount = 20_000;
    private const int ImageHeavyProductCount = 100;
    private const int ImagesPerProduct = 20;
    private const int OrderHistoryCount = 5_000;
    private const int OrderSeedBatchSize = 500;
    private const int CheckoutLineCount = Cart.MaximumLineItems;
    private const int CatalogRequestCount = 40;
    private const int RepresentativeShapeRequestCount = 20;
    private const int ManagementRequestCount = 20;
    private const int AuthRequestCount = 20;
    private const int AuthWarmupRequestCount = 4;
    private const int SessionRequestCount = 200;
    private const int CheckoutRequestCount = 12;
    private const string PerformancePassword = "Customer@123";
    private const string KeywordCatalogPath =
        "/api/products/summaries?keyword=Product%2019&page=1&pageSize=50";
    private const string ImageHeavyCatalogPath =
        "/api/products/summaries?page=1&pageSize=100";
    private const string OrderHistoryPath =
        "/api/orders/my/summaries?page=1&pageSize=50";
    private const string DashboardPath =
        "/api/admin/dashboard/summary?lowStockThreshold=10";
    private const string RevenueReportPath =
        "/api/admin/reports/revenue?groupBy=day";

    [Fact]
    [Trait("Category", "SqlServerPerformance")]
    public async Task CriticalReadAndWritePaths_MeetPerformanceBudgets()
    {
        SqlServerPerformanceTestGate.Require();

        var databaseName = $"ECommerceBackendPerformance_{Guid.NewGuid():N}";
        var connectionString =
            SqlServerPerformanceTestGate.CreateDatabaseConnectionString(databaseName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var factory = new SqlServerPerformanceApiFactory(connectionString);

        try
        {
            using var client = await factory.CreateInitializedClientAsync(timeout.Token);
            await SeedCatalogAsync(connectionString, CatalogProductCount, timeout.Token);
            await SeedCatalogImagesAsync(
                factory.Services,
                ImageHeavyProductCount,
                ImagesPerProduct,
                timeout.Token);

            var catalogPlan = await GetCatalogQueryPlanAsync(connectionString, timeout.Token);
            Assert.Contains(
                "IX_Products_IsDeleted_CreatedAt_Id",
                catalogPlan,
                StringComparison.Ordinal);

            var catalog = await MeasureGetAsync(
                client,
                "/api/products?page=1&pageSize=12",
                accessToken: null,
                warmupCount: 8,
                requestCount: CatalogRequestCount,
                concurrency: 8,
                timeout.Token);

            var keywordCatalog = await MeasureGetAsync(
                client,
                KeywordCatalogPath,
                accessToken: null,
                warmupCount: 4,
                requestCount: RepresentativeShapeRequestCount,
                concurrency: 4,
                timeout.Token);

            await AssertImageHeavyCatalogShapeAsync(client, timeout.Token);
            var imageHeavyCatalog = await MeasureGetAsync(
                client,
                ImageHeavyCatalogPath,
                accessToken: null,
                warmupCount: 4,
                requestCount: RepresentativeShapeRequestCount,
                concurrency: 4,
                timeout.Token);

            var orderHistoryCustomer = await RegisterAsync(
                factory.Services,
                "history",
                timeout.Token);
            await SeedOrderHistoryAsync(
                factory.Services,
                orderHistoryCustomer.UserId,
                OrderHistoryCount,
                timeout.Token);
            await AssertOrderHistoryShapeAsync(
                client,
                orderHistoryCustomer,
                timeout.Token);
            var orderHistory = await MeasureGetAsync(
                client,
                OrderHistoryPath,
                orderHistoryCustomer.AccessToken,
                warmupCount: 4,
                requestCount: RepresentativeShapeRequestCount,
                concurrency: 4,
                timeout.Token);

            var administrator = await RegisterAdminAsync(
                factory.Services,
                timeout.Token);
            var dashboard = await MeasureGetAsync(
                client,
                DashboardPath,
                administrator.AccessToken,
                warmupCount: 4,
                requestCount: ManagementRequestCount,
                concurrency: 4,
                timeout.Token);
            var revenueReport = await MeasureGetAsync(
                client,
                RevenueReportPath,
                administrator.AccessToken,
                warmupCount: 4,
                requestCount: ManagementRequestCount,
                concurrency: 4,
                timeout.Token);

            var loginUsers = await RegisterManyAsync(
                factory.Services,
                "login",
                AuthWarmupRequestCount + AuthRequestCount,
                timeout.Token);
            var login = await MeasurePostAsync(
                client,
                "/api/auth/login",
                loginUsers
                    .Take(AuthWarmupRequestCount)
                    .Select(user => new LoginRequest
                    {
                        UserName = user.UserName,
                        Password = PerformancePassword
                    })
                    .ToArray(),
                loginUsers
                    .Skip(AuthWarmupRequestCount)
                    .Select(user => new LoginRequest
                    {
                        UserName = user.UserName,
                        Password = PerformancePassword
                    })
                    .ToArray(),
                concurrency: 4,
                timeout.Token);

            var refreshUsers = await RegisterManyAsync(
                factory.Services,
                "refresh",
                AuthWarmupRequestCount + AuthRequestCount,
                timeout.Token);
            var refresh = await MeasurePostAsync(
                client,
                "/api/auth/refresh",
                refreshUsers
                    .Take(AuthWarmupRequestCount)
                    .Select(user => new RefreshTokenRequest
                    {
                        RefreshToken = user.RefreshToken
                    })
                    .ToArray(),
                refreshUsers
                    .Skip(AuthWarmupRequestCount)
                    .Select(user => new RefreshTokenRequest
                    {
                        RefreshToken = user.RefreshToken
                    })
                    .ToArray(),
                concurrency: 4,
                timeout.Token);

            var customer = await RegisterAsync(factory.Services, "session", timeout.Token);
            var session = await MeasureGetAsync(
                client,
                "/api/users/me",
                customer.AccessToken,
                warmupCount: 16,
                requestCount: SessionRequestCount,
                concurrency: 16,
                timeout.Token);

            var checkoutCustomers = new List<AuthResponse>(CheckoutRequestCount + 1);
            for (var index = 0; index <= CheckoutRequestCount; index++)
            {
                checkoutCustomers.Add(
                    await RegisterAsync(factory.Services, $"checkout{index}", timeout.Token));
            }

            await SeedCheckoutCartsAsync(
                factory.Services,
                checkoutCustomers,
                CheckoutLineCount,
                timeout.Token);
            var checkoutWarmupStatus = await SendCheckoutAsync(
                client,
                checkoutCustomers[0],
                requestIndex: 0,
                timeout.Token);
            Assert.Equal(HttpStatusCode.Created, checkoutWarmupStatus);
            await AssertCheckoutLineCountAsync(
                factory.Services,
                checkoutCustomers[0].UserId,
                CheckoutLineCount,
                timeout.Token);
            var checkout = await MeasureAsync(
                CheckoutRequestCount,
                concurrency: CheckoutRequestCount,
                (index, cancellationToken) => SendCheckoutAsync(
                    client,
                    checkoutCustomers[index + 1],
                    index + 1,
                    cancellationToken),
                timeout.Token);
            Assert.All(checkout.StatusCodes, status => Assert.Equal(HttpStatusCode.Created, status));

            var budgets = PerformanceBudgets.FromEnvironment();
            await WriteResultsAsync(
                new PerformanceReport(
                    DateTimeOffset.UtcNow,
                    PerformanceEnvironment.Current(),
                    CatalogProductCount,
                    ImageHeavyProductCount,
                    ImagesPerProduct,
                    OrderHistoryCount,
                    CheckoutLineCount,
                    budgets,
                    catalog.ToResult(),
                    keywordCatalog.ToResult(),
                    imageHeavyCatalog.ToResult(),
                    orderHistory.ToResult(),
                    dashboard.ToResult(),
                    revenueReport.ToResult(),
                    login.ToResult(),
                    refresh.ToResult(),
                    session.ToResult(),
                    checkout.ToResult()),
                timeout.Token);
            Assert.True(
                catalog.P95Milliseconds <= budgets.CatalogP95Milliseconds,
                $"Catalog p95 {catalog.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.CatalogP95Milliseconds:F1} ms.");
            Assert.True(
                keywordCatalog.P95Milliseconds <= budgets.KeywordCatalogP95Milliseconds,
                $"Keyword catalog p95 {keywordCatalog.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.KeywordCatalogP95Milliseconds:F1} ms.");
            Assert.True(
                imageHeavyCatalog.P95Milliseconds <= budgets.ImageHeavyCatalogP95Milliseconds,
                $"Image-heavy catalog p95 {imageHeavyCatalog.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.ImageHeavyCatalogP95Milliseconds:F1} ms.");
            Assert.True(
                orderHistory.P95Milliseconds <= budgets.OrderHistoryP95Milliseconds,
                $"Order history p95 {orderHistory.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.OrderHistoryP95Milliseconds:F1} ms.");
            Assert.True(
                dashboard.P95Milliseconds <= budgets.DashboardP95Milliseconds,
                $"Dashboard p95 {dashboard.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.DashboardP95Milliseconds:F1} ms.");
            Assert.True(
                revenueReport.P95Milliseconds <= budgets.ReportP95Milliseconds,
                $"Report p95 {revenueReport.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.ReportP95Milliseconds:F1} ms.");
            Assert.True(
                login.P95Milliseconds <= budgets.LoginP95Milliseconds,
                $"Login p95 {login.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.LoginP95Milliseconds:F1} ms.");
            Assert.True(
                refresh.P95Milliseconds <= budgets.RefreshP95Milliseconds,
                $"Refresh p95 {refresh.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.RefreshP95Milliseconds:F1} ms.");
            Assert.True(
                session.P95Milliseconds <= budgets.SessionP95Milliseconds,
                $"Session validation p95 {session.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.SessionP95Milliseconds:F1} ms.");
            Assert.True(
                checkout.P95Milliseconds <= budgets.CheckoutP95Milliseconds,
                $"Checkout p95 {checkout.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.CheckoutP95Milliseconds:F1} ms.");
            Assert.True(
                session.ThroughputPerSecond >= budgets.SessionMinimumThroughput,
                $"Session throughput {session.ThroughputPerSecond:F1} req/s was below " +
                $"{budgets.SessionMinimumThroughput:F1} req/s.");
            Assert.True(
                checkout.ThroughputPerSecond >= budgets.CheckoutMinimumThroughput,
                $"Checkout throughput {checkout.ThroughputPerSecond:F1} req/s was below " +
                $"{budgets.CheckoutMinimumThroughput:F1} req/s.");
            Assert.True(
                login.ThroughputPerSecond >= budgets.LoginMinimumThroughput,
                $"Login throughput {login.ThroughputPerSecond:F1} req/s was below " +
                $"{budgets.LoginMinimumThroughput:F1} req/s.");
            Assert.True(
                refresh.ThroughputPerSecond >= budgets.RefreshMinimumThroughput,
                $"Refresh throughput {refresh.ThroughputPerSecond:F1} req/s was below " +
                $"{budgets.RefreshMinimumThroughput:F1} req/s.");
        }
        finally
        {
            await factory.DeleteDatabaseAsync(CancellationToken.None);
        }
    }

    private static async Task<AuthResponse> RegisterAsync(
        IServiceProvider services,
        string prefix,
        CancellationToken cancellationToken)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        await using var scope = services.CreateAsyncScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        return await authService.RegisterAsync(
            new RegisterRequest
            {
                UserName = $"{prefix}_{suffix}",
                Email = $"{prefix}_{suffix}@example.com",
                Password = PerformancePassword,
                FullName = "Performance Customer"
            },
            cancellationToken);
    }

    private static async Task<IReadOnlyList<AuthResponse>> RegisterManyAsync(
        IServiceProvider services,
        string prefix,
        int count,
        CancellationToken cancellationToken)
    {
        var users = new List<AuthResponse>(count);
        for (var index = 0; index < count; index++)
        {
            users.Add(await RegisterAsync(
                services,
                $"{prefix}{index}",
                cancellationToken));
        }

        return users;
    }

    private static async Task<AuthResponse> RegisterAdminAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var registered = await RegisterAsync(services, "admin", cancellationToken);
        await using (var scope = services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await context.Users
                .Include(candidate => candidate.UserRoles)
                .SingleAsync(candidate => candidate.Id == registered.UserId, cancellationToken);
            var role = await context.Roles
                .SingleAsync(candidate => candidate.Name == RoleNames.Admin, cancellationToken);
            user.ChangeRole(role);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using var loginScope = services.CreateAsyncScope();
        var authService = loginScope.ServiceProvider.GetRequiredService<IAuthService>();
        return await authService.LoginAsync(
            new LoginRequest
            {
                UserName = registered.UserName,
                Password = PerformancePassword
            },
            cancellationToken);
    }

    private static async Task SeedCatalogAsync(
        string connectionString,
        int productCount,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText =
            """
            SET QUOTED_IDENTIFIER ON;
            SET ANSI_NULLS ON;
            SET ANSI_PADDING ON;
            SET ANSI_WARNINGS ON;
            SET CONCAT_NULL_YIELDS_NULL ON;
            SET ARITHABORT ON;
            SET NUMERIC_ROUNDABORT OFF;

            DECLARE @CategoryId uniqueidentifier = NEWID();
            INSERT dbo.Categories (Id, Name, ParentId, IsDeleted, NormalizedName)
            VALUES (@CategoryId, N'Performance Catalog', NULL, 0, N'PERFORMANCE CATALOG');

            ;WITH Digits (Value) AS
            (
                SELECT Value
                FROM (VALUES
                    (0), (1), (2), (3), (4),
                    (5), (6), (7), (8), (9)
                ) AS values_source (Value)
            ),
            Numbers (Value) AS
            (
                SELECT
                    ones.Value
                    + (tens.Value * 10)
                    + (hundreds.Value * 100)
                    + (thousands.Value * 1000)
                    + (ten_thousands.Value * 10000)
                    + 1
                FROM Digits AS ones
                CROSS JOIN Digits AS tens
                CROSS JOIN Digits AS hundreds
                CROSS JOIN Digits AS thousands
                CROSS JOIN Digits AS ten_thousands
            )
            INSERT dbo.Products
                (Id, CategoryId, Name, Price, StockQuantity, Description, IsDeleted, CreatedAt)
            SELECT
                NEWID(),
                @CategoryId,
                CONCAT(N'Performance Product ', numbers.Value),
                CAST(100000 + numbers.Value AS decimal(18, 2)),
                numbers.Value % 200,
                N'Representative catalog description',
                CASE WHEN numbers.Value % 100 = 0 THEN 1 ELSE 0 END,
                DATEADD(second, -numbers.Value, SYSUTCDATETIME())
            FROM Numbers AS numbers
            WHERE numbers.Value <= @ProductCount;
            """;
        command.Parameters.AddWithValue("@ProductCount", productCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SeedCatalogImagesAsync(
        IServiceProvider services,
        int productCount,
        int imagesPerProduct,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var productIds = await context.Products
            .AsNoTracking()
            .Where(product => !product.IsDeleted)
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Take(productCount)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);
        Assert.Equal(productCount, productIds.Count);

        var images = productIds
            .SelectMany(productId => Enumerable
                .Range(1, imagesPerProduct)
                .Select(imageNumber => new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = $"/uploads/products/performance-{productId:N}-{imageNumber}.jpg",
                    IsMain = imageNumber == 1
                }))
            .ToArray();
        context.ProductImages.AddRange(images);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task AssertImageHeavyCatalogShapeAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            ImageHeavyCatalogPath,
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<
            PagedResult<ProductSummaryResponse>>(cancellationToken);

        Assert.NotNull(page);
        var items = page.Items.ToArray();
        Assert.Equal(ImageHeavyProductCount, items.Length);
        Assert.All(
            items,
            item => Assert.False(string.IsNullOrWhiteSpace(item.MainImageUrl)));
    }

    private static async Task SeedOrderHistoryAsync(
        IServiceProvider services,
        Guid userId,
        int orderCount,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var productId = await context.Products
            .AsNoTracking()
            .Where(product => !product.IsDeleted)
            .OrderByDescending(product => product.CreatedAt)
            .ThenByDescending(product => product.Id)
            .Select(product => product.Id)
            .FirstAsync(cancellationToken);
        var measuredAt = DateTime.UtcNow;
        var requestHash = new string('A', 64);

        for (var batchStart = 0; batchStart < orderCount; batchStart += OrderSeedBatchSize)
        {
            var batchSize = Math.Min(OrderSeedBatchSize, orderCount - batchStart);
            var orders = new List<Order>(batchSize);
            for (var offset = 0; offset < batchSize; offset++)
            {
                var sequenceNumber = batchStart + offset + 1;
                var orderDate = measuredAt.AddSeconds(-sequenceNumber);
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    Method = PaymentMethod.CashOnDelivery,
                    Amount = 130_000m,
                    CreatedAt = orderDate
                };
                payment.ChangeStatus(PaymentStatus.Paid, orderDate);

                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    OrderNumber = $"PERF-{sequenceNumber:D10}",
                    IdempotencyKey = $"performance-history-{sequenceNumber}",
                    IdempotencyRequestHash = requestHash,
                    ShippingMethod = ShippingMethod.Standard,
                    Currency = "VND",
                    OrderDate = orderDate,
                    ShippingAddress = "Performance Street",
                    Payment = payment
                };
                order.SetRecipient("Performance Customer", phone: null);
                order.SetPricing(
                    subtotal: 100_000m,
                    discount: 0m,
                    shipping: 30_000m,
                    tax: 0m);
                order.SetPendingExpiration(orderDate.AddMinutes(15));
                order.ChangeStatus(OrderStatus.Confirmed, payment.Status);
                order.ChangeStatus(OrderStatus.Shipping, payment.Status);
                order.ChangeStatus(OrderStatus.Delivered, payment.Status);
                order.OrderDetails.Add(new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ProductNameSnapshot = "Performance History Product",
                    Quantity = 1,
                    UnitPrice = 100_000m,
                    BaseUnitPrice = 100_000m
                });
                orders.Add(order);
            }

            context.Orders.AddRange(orders);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }
    }

    private static async Task AssertOrderHistoryShapeAsync(
        HttpClient client,
        AuthResponse customer,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OrderHistoryPath);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<
            PagedResult<OrderSummaryResponse>>(cancellationToken);

        Assert.NotNull(page);
        Assert.Equal(OrderHistoryCount, page.TotalCount);
        var items = page.Items.ToArray();
        Assert.Equal(50, items.Length);
        Assert.All(items, item => Assert.Equal(1, item.TotalItemQuantity));
        Assert.All(items, item => Assert.Equal("Paid", item.PaymentStatus));
    }

    private static async Task<string> GetCatalogQueryPlanAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var showPlanOn = connection.CreateCommand();
        showPlanOn.CommandText = "SET SHOWPLAN_XML ON;";
        await showPlanOn.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            await using var query = connection.CreateCommand();
            query.CommandText =
                """
                SELECT
                    product.Id,
                    product.CategoryId,
                    product.Name,
                    product.Price,
                    product.StockQuantity,
                    product.Description,
                    product.CreatedAt,
                    category.Name AS CategoryName
                FROM dbo.Products AS product
                INNER JOIN dbo.Categories AS category ON product.CategoryId = category.Id
                WHERE product.IsDeleted = 0 AND category.IsDeleted = 0
                ORDER BY product.CreatedAt DESC, product.Id DESC
                OFFSET 0 ROWS FETCH NEXT 12 ROWS ONLY;
                """;
            return (string?)await query.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("SQL Server did not return a query plan.");
        }
        finally
        {
            await using var showPlanOff = connection.CreateCommand();
            showPlanOff.CommandText = "SET SHOWPLAN_XML OFF;";
            await showPlanOff.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task SeedCheckoutCartsAsync(
        IServiceProvider services,
        IReadOnlyList<AuthResponse> customers,
        int lineCount,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = $"Performance Checkout {Guid.NewGuid():N}",
            NormalizedName = $"PERFORMANCE CHECKOUT {Guid.NewGuid():N}"
        };
        context.Categories.Add(category);

        foreach (var customer in customers)
        {
            var cartId = await context.Carts
                .Where(cart => cart.UserId == customer.UserId)
                .Select(cart => cart.Id)
                .SingleAsync(cancellationToken);

            for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = $"Checkout Product {customer.UserId:N} {lineIndex}",
                    Price = 125_000m,
                    StockQuantity = 10,
                    Description = "Performance checkout product",
                    CreatedAt = DateTime.UtcNow
                };
                context.Products.Add(product);
                context.CartItems.Add(new CartItem
                {
                    Id = Guid.NewGuid(),
                    CartId = cartId,
                    ProductId = product.Id,
                    Quantity = 1,
                    UnitPrice = product.Price
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task AssertCheckoutLineCountAsync(
        IServiceProvider services,
        Guid userId,
        int expectedLineCount,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lineCount = await context.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId)
            .Select(order => order.OrderDetails.Count)
            .SingleAsync(cancellationToken);

        Assert.Equal(expectedLineCount, lineCount);
    }

    private static async Task<HttpStatusCode> SendCheckoutAsync(
        HttpClient client,
        AuthResponse customer,
        int requestIndex,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new PlaceOrderRequest
            {
                ShippingAddress = $"{requestIndex + 1} Performance Street",
                PaymentMethod = PaymentMethod.CashOnDelivery
            })
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        request.Headers.Add("Idempotency-Key", $"performance-{Guid.NewGuid():N}");
        using var response = await client.SendAsync(request, cancellationToken);
        return response.StatusCode;
    }

    private static async Task<Measurement> MeasureGetAsync(
        HttpClient client,
        string path,
        string? accessToken,
        int warmupCount,
        int requestCount,
        int concurrency,
        CancellationToken cancellationToken)
    {
        async Task<HttpStatusCode> SendAsync(
            int _,
            CancellationToken requestCancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (accessToken != null)
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await client.SendAsync(
                request,
                requestCancellationToken);
            return response.StatusCode;
        }

        var warmup = await MeasureAsync(
            warmupCount,
            concurrency,
            SendAsync,
            cancellationToken);
        Assert.All(
            warmup.StatusCodes,
            status => Assert.Equal(HttpStatusCode.OK, status));
        var measurement = await MeasureAsync(
            requestCount,
            concurrency,
            SendAsync,
            cancellationToken);
        Assert.All(
            measurement.StatusCodes,
            status => Assert.Equal(HttpStatusCode.OK, status));
        return measurement;
    }

    private static async Task<Measurement> MeasurePostAsync<TRequest>(
        HttpClient client,
        string path,
        IReadOnlyList<TRequest> warmupRequests,
        IReadOnlyList<TRequest> measuredRequests,
        int concurrency,
        CancellationToken cancellationToken)
    {
        static async Task<HttpStatusCode> SendAsync(
            HttpClient httpClient,
            string requestPath,
            TRequest payload,
            CancellationToken requestCancellationToken)
        {
            using var response = await httpClient.PostAsJsonAsync(
                requestPath,
                payload,
                requestCancellationToken);
            return response.StatusCode;
        }

        var warmup = await MeasureAsync(
            warmupRequests.Count,
            concurrency,
            (index, requestCancellationToken) => SendAsync(
                client,
                path,
                warmupRequests[index],
                requestCancellationToken),
            cancellationToken);
        Assert.All(
            warmup.StatusCodes,
            status => Assert.Equal(HttpStatusCode.OK, status));

        var measurement = await MeasureAsync(
            measuredRequests.Count,
            concurrency,
            (index, requestCancellationToken) => SendAsync(
                client,
                path,
                measuredRequests[index],
                requestCancellationToken),
            cancellationToken);
        Assert.All(
            measurement.StatusCodes,
            status => Assert.Equal(HttpStatusCode.OK, status));
        return measurement;
    }

    private static async Task<Measurement> MeasureAsync(
        int requestCount,
        int concurrency,
        Func<int, CancellationToken, Task<HttpStatusCode>> operation,
        CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(concurrency, concurrency);
        var total = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, requestCount).Select(async index =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var request = Stopwatch.StartNew();
                var statusCode = await operation(index, cancellationToken);
                request.Stop();
                return (StatusCode: statusCode, ElapsedMilliseconds: request.Elapsed.TotalMilliseconds);
            }
            finally
            {
                gate.Release();
            }
        });

        var samples = await Task.WhenAll(tasks);
        total.Stop();
        var ordered = samples
            .Select(sample => sample.ElapsedMilliseconds)
            .Order()
            .ToArray();
        return new Measurement(
            GetPercentile(ordered, 0.50),
            GetPercentile(ordered, 0.95),
            GetPercentile(ordered, 0.99),
            requestCount / total.Elapsed.TotalSeconds,
            samples.Select(sample => sample.StatusCode).ToArray());
    }

    private static double GetPercentile(IReadOnlyList<double> ordered, double percentile)
    {
        var index = Math.Max(
            0,
            (int)Math.Ceiling(ordered.Count * percentile) - 1);
        return ordered[index];
    }

    private static async Task WriteResultsAsync(
        PerformanceReport report,
        CancellationToken cancellationToken)
    {
        var outputDirectory =
            Environment.GetEnvironmentVariable("ECOMMERCE_PERFORMANCE_RESULTS_DIRECTORY");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        var fullPath = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullPath);
        var json = JsonSerializer.Serialize(
            report,
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(
            Path.Combine(fullPath, "performance-results.json"),
            json,
            Encoding.UTF8,
            cancellationToken);
    }

    private sealed record Measurement(
        double P50Milliseconds,
        double P95Milliseconds,
        double P99Milliseconds,
        double ThroughputPerSecond,
        IReadOnlyList<HttpStatusCode> StatusCodes)
    {
        public PerformanceResult ToResult()
            => new(
                P50Milliseconds,
                P95Milliseconds,
                P99Milliseconds,
                ThroughputPerSecond,
                StatusCodes.Count);
    }

    private sealed record PerformanceReport(
        DateTimeOffset MeasuredAt,
        PerformanceEnvironment Environment,
        int CatalogProductCount,
        int ImageHeavyProductCount,
        int ImagesPerProduct,
        int OrderHistoryCount,
        int CheckoutLineCount,
        PerformanceBudgets Budgets,
        PerformanceResult Catalog,
        PerformanceResult KeywordCatalog,
        PerformanceResult ImageHeavyCatalog,
        PerformanceResult OrderHistory,
        PerformanceResult Dashboard,
        PerformanceResult RevenueReport,
        PerformanceResult Login,
        PerformanceResult Refresh,
        PerformanceResult SessionValidation,
        PerformanceResult Checkout);

    private sealed record PerformanceEnvironment(
        string OperatingSystem,
        string Framework,
        int ProcessorCount)
    {
        public static PerformanceEnvironment Current()
            => new(
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                Environment.ProcessorCount);
    }

    private sealed record PerformanceResult(
        double P50Milliseconds,
        double P95Milliseconds,
        double P99Milliseconds,
        double ThroughputPerSecond,
        int RequestCount);

    private sealed record PerformanceBudgets(
        double CatalogP95Milliseconds,
        double KeywordCatalogP95Milliseconds,
        double ImageHeavyCatalogP95Milliseconds,
        double OrderHistoryP95Milliseconds,
        double DashboardP95Milliseconds,
        double ReportP95Milliseconds,
        double LoginP95Milliseconds,
        double RefreshP95Milliseconds,
        double SessionP95Milliseconds,
        double CheckoutP95Milliseconds,
        double LoginMinimumThroughput,
        double RefreshMinimumThroughput,
        double SessionMinimumThroughput,
        double CheckoutMinimumThroughput)
    {
        public static PerformanceBudgets FromEnvironment()
            => new(
                ReadPositiveDouble("PERFORMANCE_CATALOG_P95_MS", 500),
                ReadPositiveDouble("PERFORMANCE_KEYWORD_CATALOG_P95_MS", 750),
                ReadPositiveDouble("PERFORMANCE_IMAGE_HEAVY_CATALOG_P95_MS", 750),
                ReadPositiveDouble("PERFORMANCE_ORDER_HISTORY_P95_MS", 750),
                ReadPositiveDouble("PERFORMANCE_DASHBOARD_P95_MS", 1_000),
                ReadPositiveDouble("PERFORMANCE_REPORT_P95_MS", 1_500),
                ReadPositiveDouble("PERFORMANCE_LOGIN_P95_MS", 1_000),
                ReadPositiveDouble("PERFORMANCE_REFRESH_P95_MS", 1_000),
                ReadPositiveDouble("PERFORMANCE_SESSION_P95_MS", 500),
                ReadPositiveDouble("PERFORMANCE_CHECKOUT_P95_MS", 2_000),
                ReadPositiveDouble("PERFORMANCE_LOGIN_MIN_RPS", 5),
                ReadPositiveDouble("PERFORMANCE_REFRESH_MIN_RPS", 5),
                ReadPositiveDouble("PERFORMANCE_SESSION_MIN_RPS", 20),
                ReadPositiveDouble("PERFORMANCE_CHECKOUT_MIN_RPS", 3));

        private static double ReadPositiveDouble(string variableName, double defaultValue)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrWhiteSpace(value))
                return defaultValue;

            if (!double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed)
                || parsed <= 0)
            {
                throw new InvalidOperationException(
                    $"{variableName} must be a positive invariant-culture number.");
            }

            return parsed;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerPerformanceTestCollection
{
    public const string Name = "SQL Server performance";
}

internal sealed class SqlServerPerformanceApiFactory : WebApplicationFactory<Program>
{
    private const string JwtKey =
        "performance-test-jwt-signing-key-with-enough-length-for-hmac";
    private const string JwtIssuer = "ECommerceBackend.PerformanceTests";
    private const string JwtAudience = "ECommerceBackend.PerformanceTests.Client";
    private readonly string _connectionString;
    private readonly SemaphoreSlim _databaseInitialization = new(1, 1);
    private bool _databaseInitialized;

    public SqlServerPerformanceApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _connectionString,
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["AdminBootstrap:Enabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["OrderLifecycle:ExpirationEnabled"] = "false",
                ["Notifications:Smtp:Enabled"] = "false",
                ["RateLimiting:Auth:PermitLimit"] = "1000",
                ["RateLimiting:Refresh:PermitLimit"] = "1000",
                ["Serilog:MinimumLevel:Default"] = "Warning"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(_connectionString));
            services.PostConfigure<JwtOptions>(options =>
            {
                options.Key = JwtKey;
                options.Issuer = JwtIssuer;
                options.Audience = JwtAudience;
            });
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.TokenValidationParameters.ValidIssuer = JwtIssuer;
                    options.TokenValidationParameters.ValidAudience = JwtAudience;
                    options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
                });
        });
    }

    public async Task<HttpClient> CreateInitializedClientAsync(CancellationToken cancellationToken)
    {
        var client = CreateClient();
        await _databaseInitialization.WaitAsync(cancellationToken);
        try
        {
            if (!_databaseInitialized)
            {
                await using var scope = Services.CreateAsyncScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await context.Database.MigrateAsync(cancellationToken);
                _databaseInitialized = true;
            }
        }
        finally
        {
            _databaseInitialization.Release();
        }

        return client;
    }

    public async Task DeleteDatabaseAsync(CancellationToken cancellationToken)
    {
        if (!_databaseInitialized)
            return;

        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await context.Database.EnsureDeletedAsync(cancellationToken);
        _databaseInitialized = false;
    }
}
