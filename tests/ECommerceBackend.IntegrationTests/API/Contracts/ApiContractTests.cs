using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ECommerceBackend.API.Errors;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;
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

[Collection(ApiContractTestCollection.Name)]
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
        var productSummaries = await client.GetAsync(
            "/api/products/summaries?page=1&pageSize=10");
        var categories = await client.GetAsync("/api/categories");
        var paymentMethods = await client.GetAsync("/api/payments/methods");
        var openApi = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, products.StatusCode);
        Assert.Equal(HttpStatusCode.OK, productSummaries.StatusCode);
        Assert.Equal(HttpStatusCode.OK, categories.StatusCode);
        Assert.Equal(HttpStatusCode.OK, paymentMethods.StatusCode);
        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.True(health.Headers.CacheControl?.NoStore);

        var healthDetails = await client.GetAsync("/health/details");
        Assert.Equal(HttpStatusCode.Unauthorized, healthDetails.StatusCode);
    }

    [Fact]
    public async Task VersionOneRoutes_PreserveLegacyRoutesAndAdvertiseVersion()
    {
        using var client = await _factory.CreateInitializedClientAsync();

        using var legacy = await client.GetAsync("/api/products?page=1&pageSize=10");
        using var versioned = await client.GetAsync("/api/v1/products?page=1&pageSize=10");
        using var openApiResponse = await client.GetAsync("/swagger/v1/swagger.json");
        using var openApi = await ReadJsonAsync(openApiResponse);

        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, versioned.StatusCode);
        Assert.Contains(
            "1.0",
            versioned.Headers.GetValues("api-supported-versions"));

        var paths = openApi.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/products", out _));
        Assert.True(paths.TryGetProperty("/api/v1/products/summaries", out _));
        Assert.False(paths.TryGetProperty("/api/products", out _));
    }

    [Fact]
    public async Task FrameworkErrors_ReturnStableVietnameseProblemDetails()
    {
        using var client = await _factory.CreateInitializedClientAsync();

        await AssertVietnameseProblemAsync(
            await client.GetAsync("/api/not-a-real-endpoint"),
            HttpStatusCode.NotFound,
            "endpoint_not_found",
            "Không tìm thấy endpoint được yêu cầu.");
        await AssertVietnameseProblemAsync(
            await client.GetAsync("/api/v2/products"),
            HttpStatusCode.NotFound,
            "endpoint_not_found",
            "Không tìm thấy endpoint được yêu cầu.");
        await AssertVietnameseProblemAsync(
            await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Patch,
                "/api/products")),
            HttpStatusCode.MethodNotAllowed,
            "method_not_allowed",
            "Phương thức HTTP không được hỗ trợ cho endpoint này.");
        await AssertVietnameseProblemAsync(
            await client.PostAsync(
                "/api/auth/login",
                new StringContent("plain", Encoding.UTF8, "text/plain")),
            HttpStatusCode.UnsupportedMediaType,
            "unsupported_media_type",
            "Định dạng dữ liệu gửi lên không được hỗ trợ.");
    }

    [Fact]
    public async Task OpenApi_DescribesSecurityAndCriticalRequestContracts()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = await ReadJsonAsync(response);
        var paths = document.RootElement.GetProperty("paths");

        var checkout = paths.GetProperty("/api/v1/orders").GetProperty("post");
        Assert.True(checkout.TryGetProperty("security", out _));
        AssertRequiredParameter(checkout, "Idempotency-Key", "header");
        var quote = paths
            .GetProperty("/api/v1/orders/quote")
            .GetProperty("post");
        Assert.True(quote.TryGetProperty("security", out _));
        var promotions = paths
            .GetProperty("/api/v1/promotions")
            .GetProperty("get");
        Assert.True(promotions.TryGetProperty("security", out _));
        foreach (var fulfillmentPath in new[]
        {
            "/api/v1/orders/{id}/shipment/dispatch",
            "/api/v1/orders/{id}/shipment/deliver",
            "/api/v1/orders/{id}/return-request",
            "/api/v1/orders/{id}/return-request/review",
            "/api/v1/orders/{id}/return-request/receive"
        })
        {
            var operation = paths
                .GetProperty(fulfillmentPath)
                .GetProperty("post");
            Assert.True(
                operation.TryGetProperty("security", out _),
                $"Missing security contract for {fulfillmentPath}.");
            AssertRequiredParameter(operation, "id", "path");
        }

        var publicProducts = paths.GetProperty("/api/v1/products").GetProperty("get");
        Assert.False(publicProducts.TryGetProperty("security", out _));
        var publicProductSummaries = paths
            .GetProperty("/api/v1/products/summaries")
            .GetProperty("get");
        Assert.False(publicProductSummaries.TryGetProperty("security", out _));

        var customerOrderSummaries = paths
            .GetProperty("/api/v1/orders/my/summaries")
            .GetProperty("get");
        Assert.True(customerOrderSummaries.TryGetProperty("security", out _));
        var staffOrderSummaries = paths
            .GetProperty("/api/v1/orders/summaries")
            .GetProperty("get");
        Assert.True(staffOrderSummaries.TryGetProperty("security", out _));

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        var productSummaryProperties = schemas
            .GetProperty(nameof(ProductSummaryResponse))
            .GetProperty("properties");
        Assert.True(productSummaryProperties.TryGetProperty("mainImageUrl", out _));
        Assert.False(productSummaryProperties.TryGetProperty("description", out _));
        Assert.False(productSummaryProperties.TryGetProperty("images", out _));
        var orderSummaryProperties = schemas
            .GetProperty(nameof(OrderSummaryResponse))
            .GetProperty("properties");
        Assert.True(orderSummaryProperties.TryGetProperty("totalItemQuantity", out _));
        Assert.False(orderSummaryProperties.TryGetProperty("orderDetails", out _));
        Assert.False(orderSummaryProperties.TryGetProperty("statusHistory", out _));

        var stockAdjustment = paths
            .GetProperty("/api/v1/products/{id}/stock")
            .GetProperty("put");
        Assert.True(stockAdjustment.TryGetProperty("security", out _));
        AssertRequiredParameter(stockAdjustment, "id", "path");
        AssertRequiredParameter(stockAdjustment, "If-Match", "header");
        Assert.True(stockAdjustment
            .GetProperty("responses")
            .TryGetProperty("428", out _));

        var webhook = paths
            .GetProperty("/api/v1/payments/webhooks/{providerCode}")
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
        Assert.True(responses
            .GetProperty("429")
            .GetProperty("headers")
            .TryGetProperty("Retry-After", out _));

        var stripeWebhook = paths
            .GetProperty("/api/v1/payments/webhooks/stripe")
            .GetProperty("post");
        Assert.False(stripeWebhook.TryGetProperty("security", out _));
        AssertRequiredParameter(
            stripeWebhook,
            "Stripe-Signature",
            "header");

        var initializePayment = paths
            .GetProperty("/api/v1/payments/orders/{orderId}/initialize")
            .GetProperty("post");
        Assert.True(initializePayment.TryGetProperty("security", out _));
        AssertRequiredParameter(initializePayment, "orderId", "path");
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
    public async Task AuthRateLimit_UsesConfiguredPermitAndStableProblemDetails()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Auth:PermitLimit"] = "1",
                    ["RateLimiting:Auth:WindowSeconds"] = "60"
                })));
        using var client = factory.CreateClient();
        var request = new LoginRequest
        {
            UserName = string.Empty,
            Password = string.Empty
        };

        using var first = await client.PostAsJsonAsync("/api/auth/login", request);
        using var second = await client.PostAsJsonAsync("/api/auth/login", request);
        using var problem = await ReadJsonAsync(second);

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("rate_limit_exceeded", problem.RootElement.GetProperty("code").GetString());
        Assert.NotNull(second.Headers.RetryAfter);
        Assert.True(second.Headers.RetryAfter!.Delta.HasValue);
        Assert.InRange(
            second.Headers.RetryAfter.Delta.Value.TotalSeconds,
            1,
            60);
    }

    [Fact]
    public async Task ProtectedRequest_EmitsBoundedSessionMetricsWithoutTokenData()
    {
        var measurements = new ConcurrentQueue<SessionMetricMeasurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "ECommerceBackend.Auth"
                && instrument.Name is "auth.session.validations"
                    or "auth.session.validation.duration")
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Enqueue(new SessionMetricMeasurement(
                instrument.Name,
                tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Enqueue(new SessionMetricMeasurement(
                instrument.Name,
                tags.ToArray())));
        listener.Start();

        using var client = await _factory.CreateInitializedClientAsync();
        var session = await CreateRoleSessionAsync(client, RoleNames.Customer);
        SetBearerToken(client, session.AccessToken);

        using var response = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "auth.session.validations"
            && HasOutcome(measurement.Tags, "success"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "auth.session.validation.duration"
            && HasOutcome(measurement.Tags, "success"));

        using var logout = await client.PostAsJsonAsync(
            "/api/auth/logout",
            new LogoutRequest { RefreshToken = session.RefreshToken });
        using var staleSessionResponse = await client.GetAsync("/api/users/me");

        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, staleSessionResponse.StatusCode);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "auth.session.validations"
            && HasOutcome(measurement.Tags, "inactive"));
        Assert.Contains(measurements, measurement =>
            measurement.Name == "auth.session.validation.duration"
            && HasOutcome(measurement.Tags, "inactive"));
        var recordedText = string.Join(
            '|',
            measurements.SelectMany(measurement => measurement.Tags)
                .Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain(session.AccessToken, recordedText, StringComparison.Ordinal);
        Assert.DoesNotContain(session.RefreshToken, recordedText, StringComparison.Ordinal);
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
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/api/orders/my/summaries")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/orders/summaries")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/reports/sales-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/dashboard/summary")).StatusCode);
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

    [Fact]
    public async Task AdminSession_CanReachEveryOperationalArea()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        SetBearerToken(client, (await CreateRoleSessionAsync(client, RoleNames.Admin)).AccessToken);

        foreach (var path in new[]
        {
            "/api/users?page=1&pageSize=10",
            "/api/orders?page=1&pageSize=10",
            "/api/orders/summaries?page=1&pageSize=10",
            "/api/promotions?page=1&pageSize=10",
            "/api/admin/promotions/analytics?page=1&pageSize=10&sortBy=usage",
            "/api/inventory/low-stock?page=1&pageSize=10",
            "/api/admin/inventory?page=1&pageSize=10",
            "/api/admin/customers?page=1&pageSize=10",
            "/api/reports/sales-summary",
            "/api/admin/reports/revenue?groupBy=day",
            "/api/admin/reports/orders",
            "/api/admin/reports/products?limit=5",
            "/api/admin/reports/customers?limit=5",
            "/api/admin/reports/returns?reasonLimit=5",
            "/api/admin/dashboard/summary",
            "/api/admin/dashboard/revenue?groupBy=day",
            "/api/admin/dashboard/orders-by-status",
            "/api/admin/dashboard/top-products?limit=5",
            "/api/admin/dashboard/low-stock?page=1&pageSize=10",
            "/api/admin/dashboard/recent-activities?limit=5",
            "/api/operations/outbox/dead-letters?page=1&pageSize=10",
            "/api/operations/audit-events?page=1&pageSize=10",
            "/health/details"
        })
        {
            await AssertStatusAsync(client, path, HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AdminStockAdjustment_RequiresEtagAndWritesInventoryLedger()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        SetBearerToken(client, (await CreateRoleSessionAsync(client, RoleNames.Admin)).AccessToken);
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        byte[] version = [1, 2, 3, 4, 5, 6, 7, 8];
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Categories.Add(new Category
            {
                Id = categoryId,
                Name = "API inventory"
            });
            context.Products.Add(new Product
            {
                Id = productId,
                CategoryId = categoryId,
                Name = "API inventory product",
                Price = 100m,
                StockQuantity = 5,
                Description = "API inventory product",
                CreatedAt = DateTime.UtcNow,
                RowVersion = version
            });
            await context.SaveChangesAsync();
        }

        using var missingPrecondition = await client.PutAsJsonAsync(
            $"/api/products/{productId}/stock",
            new AdjustProductStockRequest
            {
                TargetQuantity = 7,
                Reason = "Kiểm kê API"
            });
        Assert.Equal((HttpStatusCode)428, missingPrecondition.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/products/{productId}/stock")
        {
            Content = JsonContent.Create(new AdjustProductStockRequest
            {
                TargetQuantity = 7,
                Reason = "  Kiểm kê API  "
            })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            $"\"{Convert.ToBase64String(version)}\"");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        Assert.Equal(
            7,
            await verificationContext.Products
                .Where(product => product.Id == productId)
                .Select(product => product.StockQuantity)
                .SingleAsync());
        var transaction = await verificationContext.InventoryTransactions
            .SingleAsync(item => item.ProductId == productId);
        Assert.Equal(InventoryTransactionType.ManualAdjustment, transaction.Type);
        Assert.Equal(2, transaction.QuantityChange);
        Assert.Equal("Kiểm kê API", transaction.Reason);
    }

    [Fact]
    public async Task AdminStockIn_RequiresEtagAndWritesTraceableInventoryLedger()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        SetBearerToken(client, (await CreateRoleSessionAsync(client, RoleNames.Admin)).AccessToken);
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        byte[] version = [1, 2, 3, 4, 5, 6, 7, 8];
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.Categories.Add(new Category
            {
                Id = categoryId,
                Name = "API stock in"
            });
            context.Products.Add(new Product
            {
                Id = productId,
                CategoryId = categoryId,
                Name = "API stock in product",
                Price = 100m,
                StockQuantity = 5,
                Description = "API stock in product",
                CreatedAt = DateTime.UtcNow,
                RowVersion = version
            });
            await context.SaveChangesAsync();
        }

        using var missingPrecondition = await client.PostAsJsonAsync(
            $"/api/admin/inventory/{productId}/stock-in",
            new StockInRequest { Quantity = 3, Reason = "Nhập hàng" });
        Assert.Equal((HttpStatusCode)428, missingPrecondition.StatusCode);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/inventory/{productId}/stock-in")
        {
            Content = JsonContent.Create(new StockInRequest
            {
                Quantity = 3,
                Reference = " GRN-API-001 ",
                Reason = " Nhập hàng API "
            })
        };
        request.Headers.TryAddWithoutValidation(
            "If-Match",
            $"\"{Convert.ToBase64String(version)}\"");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var verificationScope = _factory.Services.CreateScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<AppDbContext>();
        Assert.Equal(
            8,
            await verificationContext.Products
                .Where(product => product.Id == productId)
                .Select(product => product.StockQuantity)
                .SingleAsync());
        var transaction = await verificationContext.InventoryTransactions
            .SingleAsync(item => item.ProductId == productId);
        Assert.Equal(InventoryTransactionType.StockIn, transaction.Type);
        Assert.Equal(3, transaction.QuantityChange);
        Assert.Equal(8, transaction.BalanceAfter);
        Assert.Equal("GRN-API-001", transaction.Reference);
        Assert.Equal("Nhập hàng API", transaction.Reason);
    }

    [Fact]
    public async Task StaffSession_UsesPermissionsWithoutReceivingAdminAccess()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        SetBearerToken(client, (await CreateRoleSessionAsync(client, RoleNames.Staff)).AccessToken);

        await AssertStatusAsync(client, "/api/orders?page=1&pageSize=10", HttpStatusCode.OK);
        await AssertStatusAsync(
            client,
            "/api/orders/summaries?page=1&pageSize=10",
            HttpStatusCode.OK);
        await AssertStatusAsync(client, "/api/inventory/low-stock?page=1&pageSize=10", HttpStatusCode.OK);
        await AssertStatusAsync(client, "/api/admin/inventory?page=1&pageSize=10", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/admin/customers?page=1&pageSize=10", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/users?page=1&pageSize=10", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/promotions?page=1&pageSize=10", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/admin/promotions/analytics?page=1&pageSize=10&sortBy=usage", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/reports/sales-summary", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/admin/reports/revenue?groupBy=day", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/api/operations/audit-events", HttpStatusCode.Forbidden);
        await AssertStatusAsync(client, "/health/details", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminSession_CanReadRedactedAuditEventDetail()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        var auditEventId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            context.AuditEvents.Add(new AuditEvent
            {
                Id = auditEventId,
                Action = "user.update",
                EntityType = "User",
                EntityId = Guid.NewGuid().ToString(),
                CorrelationId = "audit-contract-test",
                MetadataJson = "{\"reason\":\"manual review\",\"accessToken\":\"secret-token\"}",
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        SetBearerToken(client, (await CreateRoleSessionAsync(client, RoleNames.Admin)).AccessToken);
        using var response = await client.GetAsync($"/api/operations/audit-events/{auditEventId}");
        using var payload = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var metadata = payload.RootElement.GetProperty("metadataJson").GetString();
        Assert.Contains("manual review", metadata, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", metadata, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminCustomerLock_RevokesCustomerSessionAndInvalidatesAccessToken()
    {
        using var client = await _factory.CreateInitializedClientAsync();
        var customer = await CreateRoleSessionAsync(client, RoleNames.Customer);
        var administrator = await CreateRoleSessionAsync(client, RoleNames.Admin);
        SetBearerToken(client, administrator.AccessToken);

        using (var lockResponse = await client.PostAsync(
                   $"/api/admin/customers/{customer.UserId}/lock",
                   content: null))
        {
            Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);
        }

        SetBearerToken(client, customer.AccessToken);
        await AssertStatusAsync(client, "/api/users/me", HttpStatusCode.Unauthorized);

        SetBearerToken(client, administrator.AccessToken);
        using var unlockResponse = await client.PostAsync(
            $"/api/admin/customers/{customer.UserId}/unlock",
            content: null);
        Assert.Equal(HttpStatusCode.OK, unlockResponse.StatusCode);

        SetBearerToken(client, customer.AccessToken);
        await AssertStatusAsync(client, "/api/users/me", HttpStatusCode.Unauthorized);
    }

    private async Task<AuthResponse> CreateRoleSessionAsync(HttpClient client, string roleName)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var userName = $"{roleName.ToLowerInvariant()}_{suffix[..12]}";
        const string password = "RoleSession@123";

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = await context.Roles.SingleAsync(candidate => candidate.Name == roleName);
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                Email = $"{userName}@example.com",
                NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                FullName = $"{roleName} API Contract",
                CreatedAt = DateTime.UtcNow
            };
            context.Users.Add(user);
            context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
            await context.SaveChangesAsync();
        }

        return await PostAuthAsync(client, "/api/auth/login", new LoginRequest
        {
            UserName = userName,
            Password = password
        });
    }

    private static async Task AssertVietnameseProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        string expectedMessage)
    {
        using (response)
        using (var problem = await ReadJsonAsync(response))
        {
            Assert.Equal(expectedStatus, response.StatusCode);
            Assert.StartsWith(
                ApiProblemDetails.ContentType,
                response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(
                expectedCode,
                problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(
                expectedMessage,
                problem.RootElement.GetProperty("message").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                problem.RootElement.GetProperty("traceId").GetString()));
        }
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

    private static bool HasOutcome(
        IReadOnlyList<KeyValuePair<string, object?>> tags,
        string expected)
        => tags.Any(tag => tag.Key == "auth.session.outcome"
            && string.Equals(tag.Value?.ToString(), expected, StringComparison.Ordinal));

    private static async Task AssertStatusAsync(
        HttpClient client,
        string path,
        HttpStatusCode expectedStatus)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

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

    private sealed record SessionMetricMeasurement(
        string Name,
        IReadOnlyList<KeyValuePair<string, object?>> Tags);
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiContractTestCollection
{
    public const string Name = "API contract host";
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
                ["Notifications:Smtp:Enabled"] = "false",
                ["RateLimiting:Auth:PermitLimit"] = "1000"
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
