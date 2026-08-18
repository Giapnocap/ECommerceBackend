using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed record PaymentReconciliationBatchResult(
        int Examined,
        int Updated,
        int Failed);

    public sealed class PaymentReconciliationUseCase
    {
        private readonly IPaymentGateway _gateway;
        private readonly IPaymentRepository _payments;
        private readonly IDataConsistencyService _consistency;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;
        private readonly StripePaymentOptions _options;
        private readonly ILogger<PaymentReconciliationUseCase> _logger;

        public PaymentReconciliationUseCase(
            IPaymentGateway gateway,
            IPaymentRepository payments,
            IDataConsistencyService consistency,
            IUnitOfWork unitOfWork,
            IOutboxWriter outbox,
            IAuditWriter audit,
            TimeProvider timeProvider,
            IOptions<StripePaymentOptions> options,
            ILogger<PaymentReconciliationUseCase> logger)
        {
            _gateway = gateway;
            _payments = payments;
            _consistency = consistency;
            _unitOfWork = unitOfWork;
            _outbox = outbox;
            _audit = audit;
            _timeProvider = timeProvider;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<PaymentReconciliationBatchResult> ExecuteBatchAsync(
            CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var candidates = await _payments.GetStaleExternalPaymentsAsync(
                now.AddMinutes(-_options.ReconciliationStaleAfterMinutes),
                _options.ReconciliationBatchSize,
                cancellationToken);
            var updated = 0;
            var failed = 0;

            foreach (var candidate in candidates)
            {
                try
                {
                    if (!string.Equals(
                            candidate.Provider,
                            _gateway.ProviderCode,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ConflictException(
                            "payment_gateway_not_supported",
                            "Cổng thanh toán của giao dịch không được hỗ trợ đối soát.");
                    }

                    var providerState = await _gateway.GetPaymentAsync(
                        candidate.ProviderPaymentId,
                        cancellationToken);
                    if (await ApplyProviderStateAsync(
                            candidate,
                            providerState,
                            cancellationToken))
                    {
                        updated++;
                    }
                }
                catch (OperationCanceledException)
                    when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(
                        ex,
                        "Payment reconciliation failed for PaymentId {PaymentId}, Provider {Provider}, ProviderPaymentId {ProviderPaymentId}",
                        candidate.PaymentId,
                        candidate.Provider,
                        candidate.ProviderPaymentId);
                }
            }

            return new PaymentReconciliationBatchResult(
                candidates.Count,
                updated,
                failed);
        }

        private async Task<bool> ApplyProviderStateAsync(
            PaymentReconciliationCandidate candidate,
            GatewayPaymentStatusResult providerState,
            CancellationToken cancellationToken)
        {
            ValidateProviderState(candidate, providerState);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var order = await _consistency.LockOrderAsync(
                    candidate.OrderId,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy đơn hàng cần đối soát thanh toán.");
                var payment = await _consistency.LockPaymentByIdAsync(
                    candidate.PaymentId,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy giao dịch cần đối soát.");

                if (payment.OrderId != order.Id
                    || !string.Equals(
                        payment.Provider,
                        candidate.Provider,
                        StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(
                        payment.ProviderTransactionId,
                        candidate.ProviderPaymentId,
                        StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "payment_reconciliation_identity_mismatch",
                        "Tham chiếu giao dịch đã thay đổi trong lúc đối soát.");
                }

                if (!payment.HasActiveExternalTransaction)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                    return false;
                }

                if (payment.Amount != providerState.Amount
                    || !string.Equals(
                        payment.Currency,
                        providerState.Currency,
                        StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "payment_reconciliation_amount_mismatch",
                        "Số tiền hoặc tiền tệ từ cổng thanh toán không khớp giao dịch nội bộ.");
                }

                var observedAt = _timeProvider.GetUtcNow().UtcDateTime;
                var statusChange = DomainRuleGuard.AsConflict(() =>
                    payment.ReconcileProviderStatus(
                        providerState.Status,
                        observedAt));
                if (statusChange.Changed)
                {
                    _payments.AddStatusHistory(new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ChangedByUserId = null,
                        FromStatus = statusChange.Previous,
                        ToStatus = statusChange.Current,
                        Source = PaymentStatusChangeSource.Reconciliation,
                        Reference = candidate.ProviderPaymentId,
                        OccurredAt = observedAt,
                        CreatedAt = observedAt
                    });
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Cập nhật thanh toán",
                        $"Thanh toán của đơn hàng {order.OrderNumber} đã được đối soát sang trạng thái {payment.Status}.",
                        order.Id,
                        payment.Id);
                    _audit.Write(
                        "payment.reconciled",
                        nameof(Payment),
                        payment.Id.ToString(),
                        metadata: new Dictionary<string, object?>
                        {
                            ["orderId"] = order.Id,
                            ["provider"] = payment.Provider,
                            ["providerPaymentId"] = payment.ProviderTransactionId,
                            ["fromStatus"] = statusChange.Previous.ToString(),
                            ["toStatus"] = statusChange.Current.ToString()
                        });
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                return statusChange.Changed;
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private static void ValidateProviderState(
            PaymentReconciliationCandidate candidate,
            GatewayPaymentStatusResult providerState)
        {
            if (!string.Equals(
                    candidate.ProviderPaymentId,
                    providerState.ProviderPaymentId,
                    StringComparison.Ordinal)
                || providerState.Amount <= 0
                || providerState.Currency.Length != 3
                || providerState.Status is PaymentStatus.Refunded
                    or PaymentStatus.PartiallyRefunded)
            {
                throw new ConflictException(
                    "payment_reconciliation_response_invalid",
                    "Dữ liệu đối soát từ cổng thanh toán không hợp lệ.");
            }
        }
    }
}
