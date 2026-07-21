using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ECommerceBackend.API.Errors;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.Tests;

public sealed class ApiContractTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ApiContractTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublicEndpoints_ReturnExpectedContracts()
    {
        using var client = await _factory.CreateInitializedClientAsync();

        var health = await client.GetAsync("/health/live");
        var products = await client.GetAsync("/api/products?page=1&pageSize=10");
        var categories = await client.GetAsync("/api/categories");
        var paymentMethods = await client.GetAsync("/api/payments/methods");
        var openApi = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paymentMethods.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.True(health.Headers.CacheControl?.NoStore);

        var healthDetails = await client.GetAsync("/health/details");
        Assert.Equal(HttpStatusCode.Unauthorized, healthDetails.StatusCode);
    }

    [Fact]
    public async Task OpenApi_DescribesSecurityAndCriticalRequestContracts()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response);
        var paths = document.RootElement.GetProperty("paths");

        var checkout = paths.GetProperty("/api/orders").GetProperty("post");
        Assert.True(checkout.TryGetProperty("security", out _));
        AssertRequiredParameter(checkout, "Idempotency-Key", "header");

        var publicProducts = paths.GetProperty("/api/products").GetProperty("get");
        Assert.False(publicProducts.TryGetProperty("security", out _));

        var webhook = paths
            .GetProperty("/api/payments/webhooks/{providerCode}")
            .GetProperty("post");
        Assert.False(webhook.TryGetProperty("security", out _));
        AssertRequiredParameter(webhook, "providerCode", "path");
        AssertRequiredParameter(webhook, "X-Payment-Event-Id", "header");
        AssertRequiredParameter(webhook, "X-Payment-Signature", "header");

        var requestBody = webhook.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        Assert.True(requestBody
            .GetProperty("content")
            .TryGetProperty("application/json", out _));

        var responses = webhook.GetProperty("responses");
        foreach (var statusCode in new[] { "200", "400", "401", "409", "413", "429", "500" })
            Assert.True(responses.TryGetProperty(statusCode, out _), $"Missing webhook response {statusCode}.");
    }

    [Fact]
    public async Task AnonymousProtectedEndpoint_ReturnsStableProblemDetails()
    {
        using var client = await _factory.CreateInitializedClientAsync();

        var response = await client.GetAsync("/api/users/me");
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith(ApiProblemDetails.ContentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("unauthorized", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task InvalidRegistration_ReturnsValidationProblemDetails()
    {
        using var client = await _factory.CreateInitializedClientAsync();

        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            UserName = string.Empty,
            Email = "not-an-email",
            Password = "short",
            FullName = string.Empty
        });
        using var problem = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("UserName", out _));
    }

    [Fact]
    public async Task CustomerSessionAndCommerceEndpoints_PreserveHttpContracts()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var userName = $"api_{suffix[..12]}";
        const string password = "Customer@123";

        var registered = await PostAuthAsync(client, "/api/auth/register", new RegisterRequest
        {
            UserName = userName,
            Email = $"{userName}@example.com",
            Password = password,
            FullName = "API Contract Customer"
        });
        SetBearerToken(client, registered.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/orders/my")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reports/sales-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/health/details")).StatusCode);

        using var checkout = new HttpRequestMessage(HttpMethod.Post, "/api/orders")
        {
            Content = JsonContent.Create(new PlaceOrderRequest
            {
                ShippingAddress = "1 API Contract Street",
                PaymentMethod = PaymentMethod.CashOnDelivery
            })
        };
        checkout.Headers.Add("Idempotency-Key", $"api-{suffix}");
        var checkoutResponse = await client.SendAsync(checkout);
        using var checkoutProblem = await ReadJsonAsync(checkoutResponse);
        Assert.Equal(HttpStatusCode.BadRequest, checkoutResponse.StatusCode);
        Assert.Equal("business_error", checkoutProblem.RootElement.GetProperty("code").GetString());

        client.DefaultRequestHeaders.Authorization = null;
        var login = await PostAuthAsync(client, "/api/auth/login", new LoginRequest
        {
            UserName = userName,
            Password = password
        });
        var refreshed = await PostAuthAsync(client, "/api/auth/refresh", new RefreshTokenRequest
        {
            RefreshToken = login.RefreshToken
        });
        SetBearerToken(client, refreshed.AccessToken);
        var logout = await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest
        {
            RefreshToken = refreshed.RefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

        client.DefaultRequestHeaders.Authorization = null;
        var secondLogin = await PostAuthAsync(client, "/api/auth/login", new LoginRequest
        {
            UserName = userName,
            Password = password
        });
        SetBearerToken(client, secondLogin.AccessToken);
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsync("/api/auth/logout-all", content: null)).StatusCode);
    }

    private static async Task<AuthResponse> PostAuthAsync<TRequest>(
        HttpClient client,
        string path,
        TRequest request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("Authentication response was empty.");
    }

    private static void SetBearerToken(HttpClient client, string token)
        => client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
        => JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static void AssertRequiredParameter(
        JsonElement operation,
        string expectedName,
        string expectedLocation)
    {
        var parameter = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(item => string.Equals(
                item.GetProperty("name").GetString(),
                expectedName,
                StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expectedLocation, parameter.GetProperty("in").GetString());
        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("description").GetString()));
    }
}

public sealed class TestApiFactory : WebApplicationFactory<Program>
{
    private const string JwtKey = "api-contract-test-jwt-signing-key-with-enough-length";
    private const string JwtIssuer = "ECommerceBackend.ApiTests";
    private const string JwtAudience = "ECommerceBackend.ApiTests.Client";
    private readonly string _databaseName = $"ECommerceBackend.ApiTests.{Guid.NewGuid():N}";
    private readonly SemaphoreSlim _databaseInitialization = new(1, 1);
    private bool _databaseInitialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = JwtKey,
                ["Jwt:Issuer"] = JwtIssuer,
                ["Jwt:Audience"] = JwtAudience,
                ["AdminBootstrap:Enabled"] = "false",
                ["Outbox:Enabled"] = "false",
                ["OrderLifecycle:ExpirationEnabled"] = "false",
                ["Notifications:Smtp:Enabled"] = "false"
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
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
                    options.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(JwtKey));
                });
        });
    }

    public async Task<HttpClient> CreateInitializedClientAsync()
    {
        var client = CreateClient();
        await _databaseInitialization.WaitAsync();
        try
        {
            if (!_databaseInitialized)
            {
                using var scope = Services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await context.Database.EnsureCreatedAsync();
                _databaseInitialized = true;
            }
        }
        finally
        {
            _databaseInitialization.Release();
        }

        return client;
    }
}
