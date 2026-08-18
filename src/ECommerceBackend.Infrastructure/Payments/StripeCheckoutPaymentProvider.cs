using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class StripeCheckoutPaymentProvider : IPaymentProvider
    {
        private readonly StripePaymentOptions _options;
        private readonly TimeProvider _timeProvider;

        public StripeCheckoutPaymentProvider()
            : this(Options.Create(new StripePaymentOptions()))
        {
        }

        public StripeCheckoutPaymentProvider(
            IOptions<StripePaymentOptions> options)
            : this(options, TimeProvider.System)
        {
        }

        public StripeCheckoutPaymentProvider(
            IOptions<StripePaymentOptions> options,
            TimeProvider timeProvider)
        {
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public string Code => "stripe";
        public PaymentMethod? CheckoutMethod => PaymentMethod.Card;
        public bool SupportsWebhooks => true;
        public bool RequiresExternalInitialization => true;

        public PaymentInitializationResult Initialize(
            PaymentInitializationRequest request)
            => new(PaymentStatus.Pending, Code);

        public Task<VerifiedPaymentWebhook> VerifyWebhookAsync(
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
            {
                throw new NotFoundException(
                    "Cổng nhận webhook Stripe chưa được bật.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var timestamp = VerifySignature(request.Payload, request.Signature);
            try
            {
                using var document = JsonDocument.Parse(request.Payload);
                var root = document.RootElement;
                var eventId = RequiredString(root, "id");
                var eventType = RequiredString(root, "type");
                var occurredAt = root.TryGetProperty("created", out var created)
                    && created.TryGetInt64(out var unixSeconds)
                        ? DateTimeOffset.FromUnixTimeSeconds(unixSeconds)
                            .UtcDateTime
                        : DateTimeOffset.FromUnixTimeSeconds(timestamp)
                            .UtcDateTime;
                var paymentObject = root
                    .GetProperty("data")
                    .GetProperty("object");

                return Task.FromResult(ParseEvent(
                    eventId,
                    eventType,
                    occurredAt,
                    paymentObject));
            }
            catch (ApiException)
            {
                throw;
            }
            catch (Exception ex) when (ex is JsonException
                or KeyNotFoundException
                or InvalidOperationException
                or FormatException
                or ArgumentOutOfRangeException)
            {
                throw new ApiException(
                    400,
                    "invalid_stripe_webhook",
                    "Nội dung webhook Stripe không hợp lệ.",
                    innerException: ex);
            }
        }

        private VerifiedPaymentWebhook ParseEvent(
            string eventId,
            string eventType,
            DateTime occurredAt,
            JsonElement paymentObject)
        {
            if (eventType == "charge.refunded")
            {
                var providerPaymentId = RequiredString(
                    paymentObject,
                    "payment_intent");
                var currency = RequiredString(
                    paymentObject,
                    "currency").ToUpperInvariant();
                var amount = FromMinorUnits(
                    RequiredInt64(paymentObject, "amount"),
                    currency);
                var refundedAmount = FromMinorUnits(
                    RequiredInt64(paymentObject, "amount_refunded"),
                    currency);
                var refundStatus = refundedAmount == amount
                    ? PaymentStatus.Refunded
                    : PaymentStatus.PartiallyRefunded;
                return new VerifiedPaymentWebhook(
                    providerPaymentId,
                    refundStatus,
                    occurredAt,
                    amount,
                    currency,
                    refundedAmount,
                    eventId,
                    eventType);
            }

            if (!eventType.StartsWith(
                    "payment_intent.",
                    StringComparison.Ordinal))
            {
                throw new ApiException(
                    400,
                    "unsupported_stripe_event",
                    "Loại sự kiện Stripe chưa được hỗ trợ.");
            }

            var providerTransactionId = RequiredString(paymentObject, "id");
            var paymentCurrency = RequiredString(
                paymentObject,
                "currency").ToUpperInvariant();
            var paymentAmount = FromMinorUnits(
                RequiredInt64(paymentObject, "amount"),
                paymentCurrency);
            var status = eventType switch
            {
                "payment_intent.succeeded" => PaymentStatus.Paid,
                "payment_intent.processing" => PaymentStatus.Processing,
                "payment_intent.payment_failed" => PaymentStatus.Failed,
                "payment_intent.canceled" => PaymentStatus.Cancelled,
                "payment_intent.requires_action" => PaymentStatus.RequiresAction,
                "payment_intent.created" => PaymentStatus.Pending,
                _ => throw new ApiException(
                    400,
                    "unsupported_stripe_event",
                    "Loại sự kiện Stripe chưa được hỗ trợ.")
            };
            return new VerifiedPaymentWebhook(
                providerTransactionId,
                status,
                occurredAt,
                paymentAmount,
                paymentCurrency,
                ProviderEventId: eventId,
                EventType: eventType);
        }

        private long VerifySignature(string payload, string signature)
        {
            long? timestamp = null;
            var signatures = new List<byte[]>();
            foreach (var item in signature.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = item.Split('=', 2, StringSplitOptions.TrimEntries);
                if (pair.Length != 2)
                    continue;
                if (pair[0] == "t"
                    && long.TryParse(
                        pair[1],
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
                else if (pair[0] == "v1")
                {
                    try
                    {
                        signatures.Add(Convert.FromHexString(pair[1]));
                    }
                    catch (FormatException)
                    {
                        throw InvalidSignature();
                    }
                }
            }

            if (!timestamp.HasValue || signatures.Count == 0)
                throw InvalidSignature();

            var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
            if (Math.Abs(now - timestamp.Value)
                > _options.WebhookToleranceSeconds)
            {
                throw InvalidSignature();
            }

            var signedPayload = $"{timestamp.Value}.{payload}";
            using var hmac = new HMACSHA256(
                Encoding.UTF8.GetBytes(_options.WebhookSecret));
            var expected = hmac.ComputeHash(
                Encoding.UTF8.GetBytes(signedPayload));
            if (!signatures.Any(candidate =>
                    candidate.Length == expected.Length
                    && CryptographicOperations.FixedTimeEquals(
                        candidate,
                        expected)))
            {
                throw InvalidSignature();
            }

            return timestamp.Value;
        }

        private static decimal FromMinorUnits(long amount, string currency)
        {
            if (!CurrencyCatalog.IsSupported(currency))
            {
                throw new ApiException(
                    400,
                    "stripe_currency_unsupported",
                    "Webhook Stripe sử dụng tiền tệ chưa được hỗ trợ.");
            }

            if (amount <= 0)
            {
                throw new ApiException(
                    400,
                    "invalid_stripe_amount",
                    "Số tiền trong webhook Stripe không hợp lệ.");
            }

            return CurrencyCatalog.Get(currency).FromMinorUnits(amount);
        }

        private static string RequiredString(
            JsonElement element,
            string propertyName)
            => element.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()!
                    : throw new InvalidOperationException(
                        $"Stripe property '{propertyName}' is missing.");

        private static long RequiredInt64(
            JsonElement element,
            string propertyName)
            => element.TryGetProperty(propertyName, out var value)
                && value.TryGetInt64(out var result)
                    ? result
                    : throw new InvalidOperationException(
                        $"Stripe property '{propertyName}' is missing.");

        private static ApiException InvalidSignature()
            => new(
                401,
                "invalid_stripe_signature",
                "Chữ ký webhook Stripe không hợp lệ.");
    }
}
