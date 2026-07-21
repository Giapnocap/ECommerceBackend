using System.Data;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class PaymentWebhookService : IPaymentWebhookService
    {
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IPaymentProviderResolver _providers;
        private readonly IOutboxWriter _outbox;
        private readonly PaymentWebhookOptions _options;
        private readonly TimeProvider _timeProvider;

        public PaymentWebhookService(
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPaymentProviderResolver providers,
            IOutboxWriter outbox,
            IOptions<PaymentWebhookOptions> options)
            : this(
                context,
                consistency,
                providers,
                outbox,
                options,
                TimeProvider.System)
        {
        }

        public PaymentWebhookService(
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPaymentProviderResolver providers,
            IOutboxWriter outbox,
            IOptions<PaymentWebhookOptions> options,
            TimeProvider timeProvider)
        {
            _context = context;
            _consistency = consistency;
            _providers = providers;
            _outbox = outbox;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<PaymentWebhookResponse> HandleAsync(
            string providerCode,
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            ValidateRequest(providerCode, request);
            var receivedAt = UtcNow;
            var provider = _providers.GetWebhookProvider(providerCode);
            var normalizedEventId = request.EventId.Trim();
            var verified = await provider.VerifyWebhookAsync(
                request with { EventId = normalizedEventId },
                cancellationToken);
            ValidateOccurrenceTime(verified.OccurredAt, receivedAt);
            var normalizedProvider = provider.Code.Trim().ToLowerInvariant();
            var payloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Payload)));

            var existing = await FindEventAsync(
                normalizedProvider,
                normalizedEventId,
                cancellationToken);
            if (existing != null)
                return BuildDuplicateResponse(existing, payloadHash);

            var paymentOrderId = await FindPaymentOrderIdAsync(
                normalizedProvider,
                verified.ProviderTransactionId,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy payment tương ứng với webhook.");

            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                existing = await FindEventAsync(
                    normalizedProvider,
                    normalizedEventId,
                    cancellationToken);
                if (existing != null)
                {
                    var response = BuildDuplicateResponse(existing, payloadHash);
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    return response;
                }

                var order = await _consistency.LockOrderAsync(paymentOrderId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng tương ứng với payment.");
                var payment = await _consistency.LockPaymentAsync(
                    normalizedProvider,
                    verified.ProviderTransactionId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy payment tương ứng với webhook.");
                if (payment.OrderId != order.Id)
                {
                    throw new ConflictException(
                        "payment_order_mismatch",
                        "Payment không còn thuộc đơn hàng đã được xác định.");
                }

                ValidatePaymentAmount(payment, verified);
                var previousStatus = payment.Status;
                var statusChanged = ApplyStatus(payment, verified);
                var processedAt = UtcNow;
                _context.PaymentWebhookEvents.Add(new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    Provider = normalizedProvider,
                    ProviderEventId = normalizedEventId,
                    PayloadHash = payloadHash,
                    Payload = request.Payload,
                    ResultingStatus = payment.Status,
                    StatusChanged = statusChanged,
                    OccurredAt = verified.OccurredAt,
                    ReceivedAt = receivedAt,
                    ProcessedAt = processedAt
                });

                if (statusChanged)
                {
                    _context.PaymentStatusHistories.Add(new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ChangedByUserId = null,
                        FromStatus = previousStatus,
                        ToStatus = payment.Status,
                        Source = PaymentStatusChangeSource.Webhook,
                        Reference = normalizedEventId,
                        OccurredAt = verified.OccurredAt,
                        CreatedAt = processedAt
                    });

                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Cập nhật thanh toán",
                        $"Thanh toán của đơn hàng đã chuyển sang trạng thái {payment.Status}.",
                        payment.OrderId,
                        payment.Id);
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                return new PaymentWebhookResponse
                {
                    EventId = normalizedEventId,
                    PaymentId = payment.Id,
                    Status = payment.Status.ToString(),
                    Duplicate = false
                };
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                var savedEvent = await FindEventAsync(
                    normalizedProvider,
                    normalizedEventId,
                    cancellationToken);
                if (savedEvent != null)
                    return BuildDuplicateResponse(savedEvent, payloadHash);

                throw new ConflictException("Payment webhook đang được xử lý bởi yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "payment_concurrency_conflict",
                    "Payment hoặc đơn hàng đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task<Guid?> FindPaymentOrderIdAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken)
            => await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.Provider == provider
                    && payment.ProviderTransactionId == providerTransactionId)
                .Select(payment => (Guid?)payment.OrderId)
                .SingleOrDefaultAsync(cancellationToken);

        private async Task<PaymentWebhookEvent?> FindEventAsync(
            string provider,
            string eventId,
            CancellationToken cancellationToken)
            => await _context.PaymentWebhookEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(webhook => webhook.Provider == provider
                    && webhook.ProviderEventId == eventId, cancellationToken);

        private static PaymentWebhookResponse BuildDuplicateResponse(
            PaymentWebhookEvent webhook,
            string payloadHash)
        {
            if (!string.Equals(webhook.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "Provider event ID đã được dùng với payload khác.");
            }

            return new PaymentWebhookResponse
            {
                EventId = webhook.ProviderEventId,
                PaymentId = webhook.PaymentId,
                Status = webhook.ResultingStatus.ToString(),
                Duplicate = true
            };
        }

        private void ValidateOccurrenceTime(DateTime occurredAt, DateTime receivedAt)
        {
            if (occurredAt > receivedAt.AddMinutes(_options.MaxFutureSkewMinutes))
            {
                throw new ApiException(
                    400,
                    "webhook_occurrence_in_future",
                    "Payment webhook occurrence time is too far in the future.");
            }
        }

        private static void ValidatePaymentAmount(
            Payment payment,
            VerifiedPaymentWebhook webhook)
        {
            if (webhook.Status is PaymentStatus.Paid or PaymentStatus.Refunded
                && !webhook.Amount.HasValue)
            {
                throw new ApiException(
                    400,
                    "invalid_webhook_amount",
                    "Paid and refunded webhooks must include amount.");
            }

            if (webhook.Amount.HasValue && webhook.Amount.Value != payment.Amount)
            {
                throw new ConflictException(
                    "payment_amount_mismatch",
                    "Webhook amount does not match the expected payment amount.");
            }
        }

        private static bool ApplyStatus(Payment payment, VerifiedPaymentWebhook webhook)
            => DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(webhook.Status, webhook.OccurredAt).Changed);
        private void ValidateRequest(string providerCode, PaymentWebhookRequest request)
        {
            if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Trim().Length > 100)
                throw new ApiException(400, "invalid_webhook", "Payment provider không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Trim().Length > 200)
                throw new ApiException(400, "invalid_webhook", "X-Payment-Event-Id không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.Signature) || request.Signature.Length > 512)
                throw new ApiException(400, "invalid_webhook", "X-Payment-Signature không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.Payload)
                || Encoding.UTF8.GetByteCount(request.Payload) > _options.MaxPayloadBytes)
            {
                throw new ApiException(400, "invalid_webhook", "Payment webhook payload không hợp lệ hoặc quá lớn.");
            }
        }
    }
}