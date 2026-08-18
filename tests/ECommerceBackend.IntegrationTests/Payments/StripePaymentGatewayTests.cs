using System.Net;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class StripePaymentGatewayTests
{
    [Fact]
    public async Task CreatePayment_UsesStripeContractAndIdempotencyKey()
    {
        var handler = new RecordingHandler(
            """
            {
              "id": "pi_test_123",
              "client_secret": "pi_test_123_secret_value",
              "status": "requires_action"
            }
            """);
        var gateway = CreateGateway(handler);
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var result = await gateway.CreatePaymentAsync(
            new GatewayPaymentCreationRequest(
                paymentId,
                orderId,
                "ORD-001",
                125_000,
                "VND",
                "payment-attempt-001"));

        Assert.Equal("pi_test_123", result.ProviderPaymentId);
        Assert.Equal("pi_test_123_secret_value", result.ClientSecret);
        Assert.Equal(PaymentStatus.RequiresAction, result.Status);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("https://api.stripe.com/v1/payment_intents", handler.Uri);
        Assert.Equal("payment-attempt-001", handler.IdempotencyKey);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.Contains("amount=125000", handler.Body);
        Assert.Contains("currency=vnd", handler.Body);
        Assert.Contains("payment_method_types%5B%5D=card", handler.Body);
        Assert.Contains(Uri.EscapeDataString(paymentId.ToString("D")), handler.Body);
        Assert.Contains(Uri.EscapeDataString(orderId.ToString("D")), handler.Body);
    }

    [Fact]
    public async Task CreatePayment_ConvertsUsdToStripeMinorUnits()
    {
        var handler = new RecordingHandler(
            """
            {
              "id": "pi_usd_123",
              "client_secret": "pi_usd_123_secret_value",
              "status": "requires_action"
            }
            """);
        var gateway = CreateGateway(handler);

        await gateway.CreatePaymentAsync(
            new GatewayPaymentCreationRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ORD-USD-001",
                12.34m,
                "USD",
                "payment-usd-001"));

        Assert.Contains("amount=1234", handler.Body);
        Assert.Contains("currency=usd", handler.Body);
    }

    [Fact]
    public async Task Refund_UsesOriginalPaymentIntentAndAmount()
    {
        var handler = new RecordingHandler(
            """
            {
              "id": "re_test_123",
              "status": "succeeded"
            }
            """);
        var gateway = CreateGateway(handler);

        var result = await gateway.RefundAsync(
            new GatewayRefundRequest(
                Guid.NewGuid(),
                "pi_test_123",
                25_000,
                "VND",
                "refund-attempt-001"));

        Assert.Equal("re_test_123", result.ProviderRefundId);
        Assert.Equal(25_000, result.Amount);
        Assert.Equal(GatewayRefundStatus.Succeeded, result.Status);
        Assert.Equal("https://api.stripe.com/v1/refunds", handler.Uri);
        Assert.Contains("payment_intent=pi_test_123", handler.Body);
        Assert.Contains("amount=25000", handler.Body);
    }

    [Fact]
    public async Task Refund_ConvertsOriginalUsdAmountToStripeMinorUnits()
    {
        var handler = new RecordingHandler(
            """
            {
              "id": "re_usd_123",
              "status": "succeeded"
            }
            """);
        var gateway = CreateGateway(handler);

        var result = await gateway.RefundAsync(
            new GatewayRefundRequest(
                Guid.NewGuid(),
                "pi_usd_123",
                5.67m,
                "USD",
                "refund-usd-001"));

        Assert.Equal(5.67m, result.Amount);
        Assert.Contains("amount=567", handler.Body);
    }

    [Fact]
    public async Task GetPayment_MapsCurrentProviderStateWithoutIdempotencyHeader()
    {
        var handler = new RecordingHandler(
            """
            {
              "id": "pi_test_reconcile",
              "amount": 125000,
              "currency": "vnd",
              "status": "succeeded"
            }
            """);
        var gateway = CreateGateway(handler);

        var result = await gateway.GetPaymentAsync("pi_test_reconcile");

        Assert.Equal("pi_test_reconcile", result.ProviderPaymentId);
        Assert.Equal(125_000, result.Amount);
        Assert.Equal("VND", result.Currency);
        Assert.Equal(PaymentStatus.Paid, result.Status);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            "https://api.stripe.com/v1/payment_intents/pi_test_reconcile",
            handler.Uri);
        Assert.Null(handler.IdempotencyKey);
        Assert.Empty(handler.Body);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsStableGatewayProblemWithoutLeakingBody()
    {
        var handler = new RecordingHandler(
            "{\"error\":{\"message\":\"sensitive provider detail\"}}",
            HttpStatusCode.BadRequest);
        var gateway = CreateGateway(handler);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            gateway.CreatePaymentAsync(new GatewayPaymentCreationRequest(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ORD-002",
                100_000,
                "VND",
                "payment-attempt-002")));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("payment_gateway_error", exception.Code);
        Assert.DoesNotContain("sensitive", exception.Message);
    }

    private static StripePaymentGateway CreateGateway(
        HttpMessageHandler handler)
        => new(
            new HttpClient(handler)
            {
                BaseAddress = new Uri("https://api.stripe.com/")
            },
            Options.Create(new StripePaymentOptions
            {
                Enabled = true,
                SecretKey = "sk_test_unit_test_secret_key_123456",
                PublishableKey = "pk_test_unit_test_public_key_123456"
            }),
            NullLogger<StripePaymentGateway>.Instance);

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode responseStatus = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri?.AbsoluteUri;
            IdempotencyKey = request.Headers.TryGetValues(
                "Idempotency-Key",
                out var idempotencyValues)
                    ? idempotencyValues.Single()
                    : null;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            Body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(responseStatus)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
