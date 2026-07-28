using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class GenericHmacPaymentProvider : IPaymentProvider
    {
        private readonly PaymentWebhookOptions _options;
        private readonly TimeProvider _timeProvider;

        public GenericHmacPaymentProvider(IOptions<PaymentWebhookOptions> options)
            : this(options, TimeProvider.System)
        {
        }

        public GenericHmacPaymentProvider(
            IOptions<PaymentWebhookOptions> options,
            TimeProvider timeProvider)
        {
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public string Code => _options.ProviderCode;
        public PaymentMethod? CheckoutMethod => null;
        public bool SupportsWebhooks => _options.Enabled;

        public PaymentInitializationResult Initialize(PaymentInitializationRequest request)
            => throw new BusinessException("Cổng HMAC chung chỉ xử lý thông báo thanh toán và không khởi tạo giao dịch.");

        public Task<VerifiedPaymentWebhook> VerifyWebhookAsync(
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                throw new NotFoundException("Cổng nhận thông báo thanh toán chưa được bật.");

            cancellationToken.ThrowIfCancellationRequested();
            VerifySignature(request.EventId, request.Payload, request.Signature);

            GenericWebhookPayload? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GenericWebhookPayload>(request.Payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                throw new ApiException(400, "invalid_webhook_payload", "Nội dung thông báo từ cổng thanh toán không hợp lệ.", innerException: ex);
            }

            if (payload == null
                || string.IsNullOrWhiteSpace(payload.ProviderTransactionId)
                || payload.ProviderTransactionId.Length > 200)
            {
                throw new ApiException(
                    400,
                    "invalid_webhook_payload",
                    "Thông báo từ cổng thanh toán thiếu mã giao dịch hợp lệ.");
            }

            var status = payload.Status?.Trim().ToLowerInvariant() switch
            {
                "paid" => PaymentStatus.Paid,
                "failed" => PaymentStatus.Failed,
                "cancelled" => PaymentStatus.Cancelled,
                "refunded" => PaymentStatus.Refunded,
                _ => throw new ApiException(
                    400,
                    "invalid_webhook_payload",
                    "Trạng thái từ cổng thanh toán chỉ chấp nhận: đã thanh toán, thất bại, đã hủy hoặc đã hoàn tiền.")
            };

            if (status is PaymentStatus.Paid or PaymentStatus.Refunded
                && !payload.Amount.HasValue)
            {
                throw InvalidAmount("Thông báo đã thanh toán hoặc đã hoàn tiền phải có số tiền.");
            }

            if (payload.Amount.HasValue
                && (payload.Amount.Value <= 0
                    || payload.Amount.Value > OrderPricingPolicy.MaxMoneyAmount
                    || decimal.Round(payload.Amount.Value, 2, MidpointRounding.ToEven) != payload.Amount.Value))
            {
                throw InvalidAmount("Số tiền từ cổng thanh toán phải lớn hơn 0 và có tối đa 2 chữ số thập phân.");
            }

            return Task.FromResult(new VerifiedPaymentWebhook(
                payload.ProviderTransactionId.Trim(),
                status,
                payload.OccurredAt?.UtcDateTime ?? _timeProvider.GetUtcNow().UtcDateTime,
                payload.Amount));
        }

        private static ApiException InvalidAmount(string message)
            => new(400, "invalid_webhook_amount", message);

        private void VerifySignature(string eventId, string payload, string signature)
        {
            var normalizedSignature = signature.Trim();
            if (normalizedSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
                normalizedSignature = normalizedSignature[7..];

            byte[] suppliedSignature;
            try
            {
                suppliedSignature = Convert.FromHexString(normalizedSignature);
            }
            catch (FormatException)
            {
                throw InvalidSignature();
            }

            var canonicalPayload = $"{eventId.Trim()}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.Secret));
            var expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
            if (suppliedSignature.Length != expectedSignature.Length
                || !CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                throw InvalidSignature();
            }
        }

        private static ApiException InvalidSignature()
            => new(401, "invalid_webhook_signature", "Chữ ký của thông báo thanh toán không hợp lệ.");

        private sealed class GenericWebhookPayload
        {
            public string ProviderTransactionId { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTimeOffset? OccurredAt { get; set; }
            public decimal? Amount { get; set; }
        }
    }
}
