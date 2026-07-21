using System.Text;
using ECommerceBackend.API.Controllers;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Infrastructure.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public class PaymentControllerTests
{
    [Fact]
    public void GetMethods_ReturnsOnlyRegisteredCheckoutProviders()
    {
        var controller = CreateController(new RecordingWebhookService(), maxPayloadBytes: 1024);

        var result = Assert.IsType<OkObjectResult>(controller.GetMethods());
        var method = Assert.Single(Assert.IsType<PaymentMethodResponse[]>(result.Value));

        Assert.Equal("CashOnDelivery", method.Method);
        Assert.Equal("cod", method.Provider);
        Assert.False(method.SupportsWebhooks);
    }

    [Fact]
    public async Task HandleWebhook_RejectsChunkedBodyOverConfiguredLimit()
    {
        var service = new RecordingWebhookService();
        var controller = CreateController(service, maxPayloadBytes: 4);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("12345"));
        controller.Request.ContentLength = null;

        var exception = await Assert.ThrowsAsync<ApiException>(() => controller.HandleWebhook(
            "generic-hmac",
            "evt-001",
            "signature",
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, exception.StatusCode);
        Assert.Equal("webhook_payload_too_large", exception.Code);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task HandleWebhook_RejectsInvalidUtf8BeforeCallingService()
    {
        var service = new RecordingWebhookService();
        var controller = CreateController(service, maxPayloadBytes: 1024);
        controller.Request.Body = new MemoryStream([0xFF, 0xFE, 0xFD]);

        var exception = await Assert.ThrowsAsync<ApiException>(() => controller.HandleWebhook(
            "generic-hmac",
            "evt-001",
            "signature",
            CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_webhook_encoding", exception.Code);
        Assert.Equal(0, service.CallCount);
    }

    [Fact]
    public async Task HandleWebhook_PreservesExactRawPayload()
    {
        const string payload = "{\n  \"status\": \"paid\"\n}";
        var service = new RecordingWebhookService();
        var controller = CreateController(service, maxPayloadBytes: 1024);
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payload));

        var result = await controller.HandleWebhook(
            "generic-hmac",
            "evt-001",
            "signature",
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(payload, service.LastRequest?.Payload);
    }

    private static PaymentController CreateController(
        RecordingWebhookService service,
        int maxPayloadBytes)
    {
        var controller = new PaymentController(
            service,
            Options.Create(new PaymentWebhookOptions
            {
                Enabled = true,
                ProviderCode = "generic-hmac",
                Secret = "test-payment-webhook-secret-32-bytes-minimum",
                MaxPayloadBytes = maxPayloadBytes
            }),
            new PaymentProviderResolver([new CashOnDeliveryPaymentProvider()]));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    private sealed class RecordingWebhookService : IPaymentWebhookService
    {
        public int CallCount { get; private set; }
        public PaymentWebhookRequest? LastRequest { get; private set; }

        public Task<PaymentWebhookResponse> HandleAsync(
            string providerCode,
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(new PaymentWebhookResponse
            {
                EventId = request.EventId,
                PaymentId = Guid.NewGuid(),
                Status = "Paid"
            });
        }
    }
}
