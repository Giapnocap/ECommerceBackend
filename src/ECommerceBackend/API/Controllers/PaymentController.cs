using System.Buffers;
using System.Security.Claims;
using System.Text;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Nhận thông báo thanh toán đã ký từ cổng được cấu hình</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/payments")]
    [Route("api/v{version:apiVersion}/payments")]
    [Produces("application/json")]
    public sealed class PaymentController : ControllerBase
    {
        private static readonly UTF8Encoding StrictUtf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        private readonly IPaymentWebhookService _webhookService;
        private readonly IPaymentCommandService _paymentCommands;
        private readonly IPaymentProviderResolver _providers;
        private readonly int _maxPayloadBytes;

        public PaymentController(
            IPaymentWebhookService webhookService,
            IPaymentCommandService paymentCommands,
            IOptions<PaymentWebhookOptions> options,
            IPaymentProviderResolver providers)
        {
            _webhookService = webhookService;
            _paymentCommands = paymentCommands;
            _providers = providers;
            _maxPayloadBytes = options.Value.MaxPayloadBytes;
        }

        private Guid CurrentUserId => Guid.Parse(
            User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private bool CanProcessOrders => User.HasClaim(
            AuthClaimTypes.Permission,
            PermissionNames.ProcessOrders);

        /// <summary>Liệt kê các phương thức thanh toán đang khả dụng khi đặt hàng</summary>
        [HttpGet("methods")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), StatusCodes.Status200OK)]
        public IActionResult GetMethods()
        {
            var methods = _providers.GetCheckoutCapabilities()
                .Select(capability => new PaymentMethodResponse
                {
                    Method = capability.Method.ToString(),
                    Provider = capability.ProviderCode,
                    SupportsWebhooks = capability.SupportsWebhooks,
                    RequiresExternalInitialization =
                        capability.RequiresExternalInitialization
                })
                .ToArray();

            return Ok(methods);
        }

        /// <summary>Khởi tạo giao dịch thanh toán trực tuyến sau khi đơn hàng đã được lưu</summary>
        [HttpPost("orders/{orderId:guid}/initialize")]
        [Authorize]
        [EnableRateLimiting("checkout")]
        [ProducesResponseType(
            typeof(ExternalPaymentResponse),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> InitializeExternalPayment(
            Guid orderId,
            CancellationToken cancellationToken)
            => Ok(await _paymentCommands.InitializeExternalPaymentAsync(
                orderId,
                CurrentUserId,
                CanProcessOrders,
                cancellationToken));

        /// <summary>Xử lý thông báo đã ký từ cổng thanh toán</summary>
        /// <remarks>Chữ ký được xác minh bằng chính xác nội dung JSON UTF-8 của yêu cầu.</remarks>
        [HttpPost("webhooks/{providerCode}")]
        [AllowAnonymous]
        [EnableRateLimiting("webhook")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(PaymentWebhookResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> HandleWebhook(
            string providerCode,
            [FromHeader(Name = "X-Payment-Event-Id")] string eventId,
            [FromHeader(Name = "X-Payment-Signature")] string signature,
            CancellationToken cancellationToken)
        {
            var payload = await ReadPayloadAsync(cancellationToken);
            var result = await _webhookService.HandleAsync(
                providerCode,
                new PaymentWebhookRequest(eventId, signature, payload),
                cancellationToken);

            return Ok(result);
        }

        /// <summary>Xử lý webhook Stripe bằng chữ ký trên raw request body</summary>
        [HttpPost("webhooks/stripe")]
        [AllowAnonymous]
        [EnableRateLimiting("webhook")]
        [Consumes("application/json")]
        [ProducesResponseType(
            typeof(PaymentWebhookResponse),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> HandleStripeWebhook(
            [FromHeader(Name = "Stripe-Signature")] string signature,
            CancellationToken cancellationToken)
        {
            var payload = await ReadPayloadAsync(cancellationToken);
            var result = await _webhookService.HandleAsync(
                "stripe",
                new PaymentWebhookRequest(
                    string.Empty,
                    signature,
                    payload),
                cancellationToken);
            return Ok(result);
        }

        private async Task<string> ReadPayloadAsync(CancellationToken cancellationToken)
        {
            if (Request.ContentLength > _maxPayloadBytes)
                throw PayloadTooLarge();

            var initialCapacity = Request.ContentLength is > 0 and <= int.MaxValue
                ? Math.Min((int)Request.ContentLength.Value, _maxPayloadBytes)
                : 0;
            using var payload = new MemoryStream(initialCapacity);
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Min(81_920, _maxPayloadBytes + 1));

            try
            {
                while (true)
                {
                    var read = await Request.Body.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0)
                        break;

                    if (payload.Length + read > _maxPayloadBytes)
                        throw PayloadTooLarge();

                    await payload.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                try
                {
                    return StrictUtf8.GetString(payload.GetBuffer(), 0, checked((int)payload.Length));
                }
                catch (DecoderFallbackException ex)
                {
                    throw new ApiException(
                        400,
                        "invalid_webhook_encoding",
                        "Thông báo từ cổng thanh toán phải sử dụng mã hóa UTF-8 hợp lệ.",
                        innerException: ex);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private static ApiException PayloadTooLarge()
            => new(
                StatusCodes.Status413PayloadTooLarge,
                "webhook_payload_too_large",
                "Nội dung thông báo từ cổng thanh toán vượt quá giới hạn cho phép.");
    }
}
