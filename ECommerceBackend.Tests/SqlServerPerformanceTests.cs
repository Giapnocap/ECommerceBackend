using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    private const int CatalogRequestCount = 40;
    private const int SessionRequestCount = 200;
    private const int CheckoutRequestCount = 12;

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

            var catalogPlan = await GetCatalogQueryPlanAsync(connectionString, timeout.Token);
            Assert.Contains(
                "IX_Products_IsDeleted_CreatedAt_Id",
                catalogPlan,
                StringComparison.Ordinal);

            async Task<HttpStatusCode> BrowseCatalogAsync(
                int _,
                CancellationToken cancellationToken)
            {
                using var response = await client.GetAsync(
                    "/api/products?page=1&pageSize=12",
                    cancellationToken);
                return response.StatusCode;
            }

            var catalogWarmup = await MeasureAsync(
                requestCount: 8,
                concurrency: 8,
                BrowseCatalogAsync,
                timeout.Token);
            Assert.All(
                catalogWarmup.StatusCodes,
                status => Assert.Equal(HttpStatusCode.OK, status));
            var catalog = await MeasureAsync(
                CatalogRequestCount,
                concurrency: 8,
                BrowseCatalogAsync,
                timeout.Token);
            Assert.All(catalog.StatusCodes, status => Assert.Equal(HttpStatusCode.OK, status));

            var customer = await RegisterAsync(factory.Services, "session", timeout.Token);
            async Task<HttpStatusCode> ValidateSessionAsync(
                int _,
                CancellationToken cancellationToken)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/users/me");
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", customer.AccessToken);
                using var response = await client.SendAsync(request, cancellationToken);
                return response.StatusCode;
            }

            var sessionWarmup = await MeasureAsync(
                requestCount: 16,
                concurrency: 16,
                ValidateSessionAsync,
                timeout.Token);
            Assert.All(
                sessionWarmup.StatusCodes,
                status => Assert.Equal(HttpStatusCode.OK, status));
            var session = await MeasureAsync(
                SessionRequestCount,
                concurrency: 16,
                ValidateSessionAsync,
                timeout.Token);
            Assert.All(session.StatusCodes, status => Assert.Equal(HttpStatusCode.OK, status));

            var checkoutCustomers = new List<AuthResponse>(CheckoutRequestCount + 1);
            for (var index = 0; index <= CheckoutRequestCount; index++)
            {
                checkoutCustomers.Add(
                    await RegisterAsync(factory.Services, $"checkout{index}", timeout.Token));
            }

            await SeedCheckoutCartsAsync(
                factory.Services,
                checkoutCustomers,
                timeout.Token);
            var checkoutWarmupStatus = await SendCheckoutAsync(
                client,
                checkoutCustomers[0],
                requestIndex: 0,
                timeout.Token);
            Assert.Equal(HttpStatusCode.Created, checkoutWarmupStatus);
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
                    CatalogProductCount,
                    budgets,
                    catalog.ToResult(),
                    session.ToResult(),
                    checkout.ToResult()),
                timeout.Token);
            Assert.True(
                catalog.P95Milliseconds <= budgets.CatalogP95Milliseconds,
                $"Catalog p95 {catalog.P95Milliseconds:F1} ms exceeded " +
                $"{budgets.CatalogP95Milliseconds:F1} ms.");
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
                Password = "Customer@123",
                FullName = "Performance Customer"
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
            FROM
            (
                SELECT TOP (@ProductCount)
                    CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS int) AS Value
                FROM sys.all_objects AS first_source
                CROSS JOIN sys.all_objects AS second_source
            ) AS numbers;
            """;
        command.Parameters.AddWithValue("@ProductCount", productCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Name = $"Checkout Product {customer.UserId:N}",
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

        await context.SaveChangesAsync(cancellationToken);
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
        var p95Index = Math.Max(0, (int)Math.Ceiling(ordered.Length * 0.95) - 1);
        return new Measurement(
            ordered[p95Index],
            requestCount / total.Elapsed.TotalSeconds,
            samples.Select(sample => sample.StatusCode).ToArray());
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
        double P95Milliseconds,
        double ThroughputPerSecond,
        IReadOnlyList<HttpStatusCode> StatusCodes)
    {
        public PerformanceResult ToResult()
            => new(P95Milliseconds, ThroughputPerSecond, StatusCodes.Count);
    }

    private sealed record PerformanceReport(
        DateTimeOffset MeasuredAt,
        int CatalogProductCount,
        PerformanceBudgets Budgets,
        PerformanceResult Catalog,
        PerformanceResult SessionValidation,
        PerformanceResult Checkout);

    private sealed record PerformanceResult(
        double P95Milliseconds,
        double ThroughputPerSecond,
        int RequestCount);

    private sealed record PerformanceBudgets(
        double CatalogP95Milliseconds,
        double SessionP95Milliseconds,
        double CheckoutP95Milliseconds,
        double SessionMinimumThroughput,
        double CheckoutMinimumThroughput)
    {
        public static PerformanceBudgets FromEnvironment()
            => new(
                ReadPositiveDouble("PERFORMANCE_CATALOG_P95_MS", 500),
                ReadPositiveDouble("PERFORMANCE_SESSION_P95_MS", 500),
                ReadPositiveDouble("PERFORMANCE_CHECKOUT_P95_MS", 2_000),
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
