using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Tests.Support;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class StripeWebhookProviderTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
    private const string WebhookSecret =
        "whsec_webhook_provider_test_secret_123456";

    [Fact]
    public async Task SignedPaymentIntentEvent_ReturnsNeutralPaymentEvent()
    {
        var provider = CreateProvider();
        const string payload =
            "{\"id\":\"evt_stripe_001\",\"type\":\"payment_intent.succeeded\","
            + "\"created\":1787047200,\"data\":{\"object\":{"
            + "\"id\":\"pi_stripe_001\",\"amount\":125000,\"currency\":\"vnd\"}}}";
        var timestamp = Now.ToUnixTimeSeconds();

        var verified = await provider.VerifyWebhookAsync(
            new PaymentWebhookRequest(
                string.Empty,
                Sign(timestamp, payload),
                payload));

        Assert.Equal("evt_stripe_001", verified.ProviderEventId);
        Assert.Equal("payment_intent.succeeded", verified.EventType);
        Assert.Equal("pi_stripe_001", verified.ProviderTransactionId);
        Assert.Equal(PaymentStatus.Paid, verified.Status);
        Assert.Equal(125_000, verified.Amount);
        Assert.Equal("VND", verified.Currency);
    }

    [Fact]
    public async Task SignedUsdEvent_ConvertsMinorUnitsToOriginalCurrencyAmount()
    {
        var provider = CreateProvider();
        const string payload =
            "{\"id\":\"evt_stripe_usd\",\"type\":\"payment_intent.succeeded\","
            + "\"created\":1787047200,\"data\":{\"object\":{"
            + "\"id\":\"pi_stripe_usd\",\"amount\":1234,\"currency\":\"usd\"}}}";
        var timestamp = Now.ToUnixTimeSeconds();

        var verified = await provider.VerifyWebhookAsync(
            new PaymentWebhookRequest(
                string.Empty,
                Sign(timestamp, payload),
                payload));

        Assert.Equal(12.34m, verified.Amount);
        Assert.Equal("USD", verified.Currency);
    }

    [Fact]
    public async Task TamperedPayload_IsRejectedBeforeParsingBusinessData()
    {
        var provider = CreateProvider();
        const string signedPayload =
            "{\"id\":\"evt_stripe_002\",\"type\":\"payment_intent.succeeded\"}";
        var timestamp = Now.ToUnixTimeSeconds();

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            provider.VerifyWebhookAsync(new PaymentWebhookRequest(
                string.Empty,
                Sign(timestamp, signedPayload),
                signedPayload + " ")));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("invalid_stripe_signature", exception.Code);
    }

    [Fact]
    public async Task OldSignature_IsRejected()
    {
        var provider = CreateProvider();
        const string payload = "{\"id\":\"evt_old\"}";
        var timestamp = Now.AddMinutes(-10).ToUnixTimeSeconds();

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            provider.VerifyWebhookAsync(new PaymentWebhookRequest(
                string.Empty,
                Sign(timestamp, payload),
                payload)));

        Assert.Equal("invalid_stripe_signature", exception.Code);
    }

    private static StripeCheckoutPaymentProvider CreateProvider()
        => new(
            Options.Create(new StripePaymentOptions
            {
                Enabled = true,
                WebhookSecret = WebhookSecret,
                WebhookToleranceSeconds = 300
            }),
            new FixedTimeProvider(Now));

    private static string Sign(long timestamp, string payload)
    {
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(WebhookSecret));
        var signature = Convert.ToHexString(
            hmac.ComputeHash(
                Encoding.UTF8.GetBytes($"{timestamp}.{payload}")))
            .ToLowerInvariant();
        return $"t={timestamp},v1={signature}";
    }
}
