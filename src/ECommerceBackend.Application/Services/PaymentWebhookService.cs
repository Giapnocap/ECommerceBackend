using System.Data;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class PaymentWebhookService : IPaymentWebhookService
    {
        private readonly IPaymentRepository _paymentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IPaymentProviderResolver _providers;
        private readonly IOutboxWriter _outbox;
        private readonly PaymentWebhookOptions _options;
        private readonly TimeProvider _timeProvider;

        public PaymentWebhookService(
            IPaymentRepository paymentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IPaymentProviderResolver providers,
            IOutboxWriter outbox,
            IOptions<PaymentWebhookOptions> options)
            : this(
                paymentRepository,
                unitOfWork,
                consistency,
                providers,
                outbox,
                options,
                TimeProvider.System)
        {
        }

        public PaymentWebhookService(
            IPaymentRepository paymentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IPaymentProviderResolver providers,
            IOutboxWriter outbox,
            IOptions<PaymentWebhookOptions> options,
            TimeProvider timeProvider)
        {
            _paymentRepository = paymentRepository;
            _unitOfWork = unitOfWork;
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
            var normalizedProvider = provider.Code.Trim().ToLowerInvariant();
            using var telemetry = BusinessTelemetry.Start(
                "payment.webhook.process",
                cancellationToken,
                new KeyValuePair<string, object?>(
                    "payment.provider",
                    normalizedProvider));
            var normalizedEventId = request.EventId.Trim();
            var verified = await provider.VerifyWebhookAsync(
                request with { EventId = normalizedEventId },
                cancellationToken);
            ValidateOccurrenceTime(verified.OccurredAt, receivedAt);
            var payloadHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(request.Payload)));

            var existing = await FindEventAsync(
                normalizedProvider,
                normalizedEventId,
                cancellationToken);
            if (existing != null)
            {
                var response = BuildDuplicateResponse(existing, payloadHash);
                telemetry.SetTag("webhook.duplicate", true);
                telemetry.Complete();
                return response;
            }

            var paymentOrderId = await FindPaymentOrderIdAsync(
                normalizedProvider,
                verified.ProviderTransactionId,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy giao dịch thanh toán tương ứng với thông báo từ cổng thanh toán.");

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
                    var duplicateResponse = BuildDuplicateResponse(existing, payloadHash);
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    telemetry.SetTag("webhook.duplicate", true);
                    telemetry.Complete();
                    return duplicateResponse;
                }

                var order = await _consistency.LockOrderAsync(paymentOrderId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng tương ứng với giao dịch thanh toán.");
                var payment = await _consistency.LockPaymentAsync(
                    normalizedProvider,
                    verified.ProviderTransactionId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy giao dịch thanh toán tương ứng với thông báo từ cổng thanh toán.");
                if (payment.OrderId != order.Id)
                {
                    throw new ConflictException(
                        "payment_order_mismatch",
                        "Giao dịch thanh toán không còn thuộc đơn hàng đã được xác định.");
                }

                ValidatePaymentAmount(payment, verified);
                var previousStatus = payment.Status;
                var statusChanged = ApplyStatus(payment, verified);
                var processedAt = UtcNow;
                _paymentRepository.AddWebhookEvent(new PaymentWebhookEvent
                {
                    Id = Guid.NewGuid(),
                    PaymentId = payment.Id,
                    Provider = normalizedProvider,
                    ProviderEventId = normalizedEventId,
                    PayloadHash = payloadHash,
                    Payload = _options.RetainRawPayload ? request.Payload : string.Empty,
                    ResultingStatus = payment.Status,
                    StatusChanged = statusChanged,
                    OccurredAt = verified.OccurredAt,
                    ReceivedAt = receivedAt,
                    ProcessedAt = processedAt
                });

                if (statusChanged)
                {
                    _paymentRepository.AddStatusHistory(new PaymentStatusHistory
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
                        $"Thanh toán của đơn hàng đã chuyển sang trạng thái {GetPaymentStatusLabel(payment.Status)}.",
                        payment.OrderId,
                        payment.Id);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = new PaymentWebhookResponse
                {
                    EventId = normalizedEventId,
                    PaymentId = payment.Id,
                    Status = payment.Status.ToString(),
                    Duplicate = false
                };
                telemetry.SetTag("webhook.duplicate", false);
                telemetry.SetTag("payment.status", response.Status);
                telemetry.Complete();
                return response;
            }
            catch (Exception ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                var savedEvent = await FindEventAsync(
                    normalizedProvider,
                    normalizedEventId,
                    cancellationToken);
                if (savedEvent != null)
                {
                    var response = BuildDuplicateResponse(savedEvent, payloadHash);
                    telemetry.SetTag("webhook.duplicate", true);
                    telemetry.Complete();
                    return response;
                }

                throw new ConflictException("Thông báo từ cổng thanh toán đang được xử lý bởi một yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "payment_concurrency_conflict",
                    "Giao dịch thanh toán hoặc đơn hàng đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
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
            => await _paymentRepository.GetOrderIdByProviderTransactionAsync(
                provider,
                providerTransactionId,
                cancellationToken);

        private async Task<PaymentWebhookEvent?> FindEventAsync(
            string provider,
            string eventId,
            CancellationToken cancellationToken)
            => await _paymentRepository.GetWebhookEventAsync(
                provider,
                eventId,
                cancellationToken);

        private static PaymentWebhookResponse BuildDuplicateResponse(
            PaymentWebhookEvent webhook,
            string payloadHash)
        {
            if (!string.Equals(webhook.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "Mã sự kiện của cổng thanh toán đã được dùng với nội dung khác.");
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
                    "Thời điểm xảy ra giao dịch từ cổng thanh toán vượt quá giới hạn thời gian trong tương lai.");
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
                    "Thông báo đã thanh toán hoặc đã hoàn tiền phải có số tiền.");
            }

            if (webhook.Amount.HasValue && webhook.Amount.Value != payment.Amount)
            {
                throw new ConflictException(
                    "payment_amount_mismatch",
                    "Số tiền từ cổng thanh toán không khớp với số tiền cần thanh toán.");
            }
        }

        private static bool ApplyStatus(Payment payment, VerifiedPaymentWebhook webhook)
            => DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(webhook.Status, webhook.OccurredAt).Changed);
        private void ValidateRequest(string providerCode, PaymentWebhookRequest request)
        {
            if (string.IsNullOrWhiteSpace(providerCode) || providerCode.Trim().Length > 100)
                throw new ApiException(400, "invalid_webhook", "Cổng thanh toán không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Trim().Length > 200)
                throw new ApiException(400, "invalid_webhook", "X-Payment-Event-Id không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.Signature) || request.Signature.Length > 512)
                throw new ApiException(400, "invalid_webhook", "X-Payment-Signature không hợp lệ.");

            if (string.IsNullOrWhiteSpace(request.Payload)
                || Encoding.UTF8.GetByteCount(request.Payload) > _options.MaxPayloadBytes)
            {
                throw new ApiException(400, "invalid_webhook", "Nội dung thông báo từ cổng thanh toán không hợp lệ hoặc quá lớn.");
            }
        }

        private static string GetPaymentStatusLabel(PaymentStatus status)
            => status switch
            {
                PaymentStatus.Pending => "Chờ thanh toán",
                PaymentStatus.Paid => "Đã thanh toán",
                PaymentStatus.Failed => "Thất bại",
                PaymentStatus.Cancelled => "Đã hủy",
                PaymentStatus.Refunded => "Đã hoàn tiền",
                _ => status.ToString()
            };
    }
}
