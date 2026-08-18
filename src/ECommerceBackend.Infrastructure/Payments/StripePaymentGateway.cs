using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class StripePaymentGateway : IPaymentGateway
    {
        private readonly HttpClient _httpClient;
        private readonly StripePaymentOptions _options;
        private readonly ILogger<StripePaymentGateway> _logger;

        public StripePaymentGateway(
            HttpClient httpClient,
            IOptions<StripePaymentOptions> options,
            ILogger<StripePaymentGateway> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public string ProviderCode => "stripe";

        public async Task<GatewayPaymentCreationResult> CreatePaymentAsync(
            GatewayPaymentCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureEnabled();
            var amount = ToStripeMinorUnits(request.Amount, request.Currency);
            using var message = CreateRequest(
                HttpMethod.Post,
                "v1/payment_intents",
                request.IdempotencyKey,
                new Dictionary<string, string>
                {
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    ["currency"] = request.Currency.ToLowerInvariant(),
                    ["payment_method_types[]"] = "card",
                    ["metadata[payment_id]"] = request.PaymentId.ToString("D"),
                    ["metadata[order_id]"] = request.OrderId.ToString("D"),
                    ["metadata[order_number]"] = request.OrderNumber
                });
            using var document = await SendAsync(message, cancellationToken);
            var root = document.RootElement;
            var providerPaymentId = RequiredString(root, "id");
            var status = MapPaymentStatus(RequiredString(root, "status"));
            var clientSecret = OptionalString(root, "client_secret");

            _logger.LogInformation(
                "Stripe payment intent created for PaymentId {PaymentId}, OrderId {OrderId}, ProviderPaymentId {ProviderPaymentId}",
                request.PaymentId,
                request.OrderId,
                providerPaymentId);

            return new GatewayPaymentCreationResult(
                providerPaymentId,
                clientSecret,
                status);
        }

        public async Task<GatewayPaymentStatusResult> GetPaymentAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default)
        {
            EnsureEnabled();
            var normalizedPaymentId = providerPaymentId?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedPaymentId)
                || normalizedPaymentId.Length > 200)
            {
                throw new BusinessException(
                    "payment_provider_reference_invalid",
                    "Ma giao dich cua cong thanh toan khong hop le.");
            }

            using var message = CreateRequest(
                HttpMethod.Get,
                $"v1/payment_intents/{Uri.EscapeDataString(normalizedPaymentId)}");
            using var document = await SendAsync(message, cancellationToken);
            var root = document.RootElement;
            var returnedPaymentId = RequiredString(root, "id");
            var currency = RequiredString(root, "currency").ToUpperInvariant();
            var amount = FromStripeMinorUnits(
                RequiredInt64(root, "amount"),
                currency);
            var status = MapPaymentStatus(RequiredString(root, "status"));

            if (!string.Equals(
                    returnedPaymentId,
                    normalizedPaymentId,
                    StringComparison.Ordinal))
            {
                throw GatewayError();
            }

            _logger.LogInformation(
                "Stripe payment intent queried for ProviderPaymentId {ProviderPaymentId}, Status {PaymentStatus}",
                returnedPaymentId,
                status);

            return new GatewayPaymentStatusResult(
                returnedPaymentId,
                amount,
                currency,
                status);
        }

        public async Task<GatewayRefundResult> RefundAsync(
            GatewayRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureEnabled();
            var amount = ToStripeMinorUnits(request.Amount, request.Currency);
            using var message = CreateRequest(
                HttpMethod.Post,
                "v1/refunds",
                request.IdempotencyKey,
                new Dictionary<string, string>
                {
                    ["payment_intent"] = request.ProviderPaymentId,
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture),
                    ["metadata[payment_id]"] = request.PaymentId.ToString("D")
                });
            using var document = await SendAsync(message, cancellationToken);
            var root = document.RootElement;
            var providerRefundId = RequiredString(root, "id");
            var status = MapRefundStatus(RequiredString(root, "status"));

            _logger.LogInformation(
                "Stripe refund created for PaymentId {PaymentId}, ProviderRefundId {ProviderRefundId}",
                request.PaymentId,
                providerRefundId);

            return new GatewayRefundResult(
                providerRefundId,
                request.Amount,
                status);
        }

        private HttpRequestMessage CreateRequest(
            HttpMethod method,
            string path,
            string? idempotencyKey = null,
            IReadOnlyDictionary<string, string>? form = null)
        {
            var message = new HttpRequestMessage(method, path);
            if (form != null)
                message.Content = new FormUrlEncodedContent(form);
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{_options.SecretKey}:"));
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                message.Headers.Add("Idempotency-Key", idempotencyKey);
            return message;
        }

        private async Task<JsonDocument> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var requestId = response.Headers.TryGetValues(
                        "Request-Id",
                        out var values)
                        ? values.FirstOrDefault()
                        : null;
                    _logger.LogWarning(
                        "Stripe request failed with HTTP {StatusCode}, RequestId {RequestId}",
                        (int)response.StatusCode,
                        requestId);
                    throw GatewayError();
                }

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                try
                {
                    return await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw GatewayError(ex);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ApiException(
                    504,
                    "payment_gateway_timeout",
                    "Cổng thanh toán phản hồi quá thời gian cho phép.");
            }
            catch (HttpRequestException ex)
            {
                throw GatewayError(ex);
            }
        }

        private void EnsureEnabled()
        {
            if (!_options.Enabled)
            {
                throw new BusinessException(
                    "payment_gateway_disabled",
                    "Cổng thanh toán Stripe chưa được bật.");
            }
        }

        private static long ToStripeMinorUnits(
            decimal amount,
            string currency)
        {
            if (!CurrencyCatalog.IsSupported(currency))
            {
                throw new BusinessException(
                    "payment_currency_not_supported",
                    "Tiền tệ thanh toán chưa được hỗ trợ.");
            }

            long minorUnits;
            try
            {
                minorUnits = CurrencyCatalog.Get(currency)
                    .ToMinorUnits(amount);
            }
            catch (DomainRuleViolationException ex)
            {
                throw new BusinessException(
                    "payment_amount_not_supported",
                    "Số tiền không hợp lệ đối với cổng thanh toán.",
                    ex);
            }

            if (minorUnits is <= 0 or > 99_999_999)
            {
                throw new BusinessException(
                    "payment_amount_not_supported",
                    "Số tiền không hợp lệ đối với cổng thanh toán.");
            }

            return minorUnits;
        }

        private static decimal FromStripeMinorUnits(
            long amount,
            string currency)
        {
            if (!CurrencyCatalog.IsSupported(currency) || amount <= 0)
            {
                throw GatewayError();
            }

            return CurrencyCatalog.Get(currency).FromMinorUnits(amount);
        }

        private static PaymentStatus MapPaymentStatus(string status)
            => status switch
            {
                "requires_action" => PaymentStatus.RequiresAction,
                "processing" => PaymentStatus.Processing,
                "succeeded" => PaymentStatus.Paid,
                "canceled" => PaymentStatus.Cancelled,
                "requires_payment_method"
                    or "requires_confirmation"
                    or "requires_capture" => PaymentStatus.Pending,
                _ => throw GatewayError()
            };

        private static GatewayRefundStatus MapRefundStatus(string status)
            => status switch
            {
                "pending" => GatewayRefundStatus.Pending,
                "succeeded" => GatewayRefundStatus.Succeeded,
                "failed" => GatewayRefundStatus.Failed,
                "canceled" => GatewayRefundStatus.Cancelled,
                _ => throw GatewayError()
            };

        private static string RequiredString(
            JsonElement root,
            string propertyName)
            => OptionalString(root, propertyName)
                ?? throw GatewayError();

        private static long RequiredInt64(
            JsonElement root,
            string propertyName)
            => root.TryGetProperty(propertyName, out var value)
                && value.TryGetInt64(out var parsed)
                    ? parsed
                    : throw GatewayError();

        private static string? OptionalString(
            JsonElement root,
            string propertyName)
            => root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()
                    : null;

        private static ApiException GatewayError(Exception? innerException = null)
            => new(
                502,
                "payment_gateway_error",
                "Cổng thanh toán không thể xử lý yêu cầu lúc này.",
                innerException: innerException);
    }
}
