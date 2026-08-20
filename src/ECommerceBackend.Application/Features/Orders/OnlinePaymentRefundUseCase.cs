using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OnlinePaymentRefundUseCase
    {
        private readonly IPaymentGateway _gateway;
        private readonly IPaymentRepository _payments;
        private readonly IFulfillmentRepository _fulfillment;
        private readonly IOrderRepository _orders;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly OrderQueryUseCase _queries;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;
        private readonly StripePaymentOptions _options;

        public OnlinePaymentRefundUseCase(
            IPaymentGateway gateway,
            IPaymentRepository payments,
            IFulfillmentRepository fulfillment,
            IOrderRepository orders,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderQueryUseCase queries,
            IAuditWriter audit,
            TimeProvider timeProvider,
            IOptions<StripePaymentOptions> options)
        {
            _gateway = gateway;
            _payments = payments;
            _fulfillment = fulfillment;
            _orders = orders;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _queries = queries;
            _audit = audit;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            RecordOrderRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            var prepared = await PrepareAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);
            if (prepared.AlreadyCompleted)
            {
                return await _queries.GetByIdAsync(
                    orderId,
                    actorUserId,
                    true,
                    cancellationToken);
            }

            GatewayRefundResult gatewayResult;
            try
            {
                gatewayResult = await _gateway.RefundAsync(
                    new GatewayRefundRequest(
                        prepared.PaymentId,
                        prepared.ProviderPaymentId,
                        prepared.Amount,
                        prepared.Currency,
                        $"refund-{prepared.RefundId:N}"),
                    cancellationToken);
            }
            catch
            {
                await MarkPendingAsync(
                    prepared,
                    actorUserId,
                    CancellationToken.None);
                throw;
            }

            if (gatewayResult.Amount != prepared.Amount)
            {
                await MarkFailedAsync(
                    prepared,
                    actorUserId,
                    "gateway_amount_mismatch",
                    cancellationToken);
                throw new ConflictException(
                    "refund_gateway_amount_mismatch",
                    "Số tiền hoàn từ cổng thanh toán không khớp yêu cầu.");
            }

            if (gatewayResult.Status == GatewayRefundStatus.Succeeded)
            {
                return await CompleteAsync(
                    prepared,
                    actorUserId,
                    request.Note,
                    gatewayResult.ProviderRefundId,
                    cancellationToken);
            }

            if (gatewayResult.Status == GatewayRefundStatus.Pending)
            {
                await MarkPendingAsync(
                    prepared,
                    actorUserId,
                    cancellationToken);
                return await _queries.GetByIdAsync(
                    orderId,
                    actorUserId,
                    true,
                    cancellationToken);
            }

            var failureCode = gatewayResult.Status
                == GatewayRefundStatus.Cancelled
                    ? "gateway_refund_cancelled"
                    : "gateway_refund_failed";
            await MarkFailedAsync(
                prepared,
                actorUserId,
                failureCode,
                cancellationToken);
            throw new ConflictException(
                "online_refund_failed",
                "Cổng thanh toán chưa hoàn tất yêu cầu hoàn tiền.");
        }

        private async Task<PreparedRefund> PrepareAsync(
            Guid orderId,
            Guid actorUserId,
            RecordOrderRefundRequest request,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var order = await _consistency.LockOrderAsync(
                    orderId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "order_payment_missing",
                        "Đơn hàng không có giao dịch thanh toán.");
                var returnRequest = await _fulfillment
                    .LockReturnRequestByOrderIdAsync(
                        order.Id,
                        cancellationToken)
                    ?? throw new ConflictException(
                        "return_request_missing",
                        "Đơn hàng không có yêu cầu trả hàng hợp lệ.");
                EnsurePaymentMatchesOrder(order, payment);
                var reference = NormalizeReference(request.Reference);
                var existing = await _payments.GetRefundAsync(
                    payment.Id,
                    reference,
                    tracking: true,
                    cancellationToken);
                if (existing?.Status == PaymentRefundStatus.Succeeded)
                {
                    if (request.Amount.HasValue
                        && request.Amount.Value != existing.Amount)
                    {
                        throw new ConflictException(
                            "refund_idempotency_mismatch",
                            "Mã tham chiếu hoàn tiền đã được dùng với nội dung khác.");
                    }

                    if (payment.RefundedAmount < existing.Amount)
                    {
                        throw new ConflictException(
                            "refund_state_inconsistent",
                            "Trạng thái yêu cầu hoàn tiền và thanh toán không đồng nhất.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                    return BuildPrepared(
                        order,
                        payment,
                        existing,
                        alreadyCompleted: true);
                }

                ValidateRefundState(order, payment, returnRequest);
                var reservedAmount = await _payments
                    .GetReservedRefundAmountAsync(
                        payment.Id,
                        existing?.Id,
                        cancellationToken);
                var reservedBaseAmount = await _payments
                    .GetReservedRefundBaseAmountAsync(
                        payment.Id,
                        existing?.Id,
                        cancellationToken);
                var succeededBaseAmount = await _payments
                    .GetSucceededRefundBaseAmountAsync(
                        payment.Id,
                        cancellationToken);
                var availableAmount = payment.Amount
                    - payment.RefundedAmount
                    - reservedAmount;
                var availableBaseAmount = order.BaseTotalAmount
                    - succeededBaseAmount
                    - reservedBaseAmount;
                var requestedAmount = request.Amount
                    ?? (existing?.Amount ?? availableAmount);
                if (requestedAmount <= 0
                    || requestedAmount > availableAmount
                    || availableBaseAmount <= 0)
                {
                    throw new ConflictException(
                        "refund_amount_exceeds_available",
                        "Số tiền hoàn vượt quá số tiền còn có thể hoàn.");
                }

                _ = DomainRuleGuard.AsBusiness(() =>
                    new Money(requestedAmount, payment.Currency));
                var requestedBaseAmount = existing?.BaseAmount
                    ?? DomainRuleGuard.AsBusiness(() =>
                        CalculateBaseRefundAmount(
                            order,
                            payment,
                            requestedAmount,
                            availableAmount,
                            availableBaseAmount));
                if (requestedBaseAmount > availableBaseAmount)
                {
                    throw new ConflictException(
                        "refund_base_amount_exceeds_available",
                        "Số tiền hoàn quy đổi vượt quá số tiền còn có thể hoàn.");
                }

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var refund = existing ?? DomainRuleGuard.AsBusiness(() =>
                    PaymentRefund.Create(
                        Guid.NewGuid(),
                        payment.Id,
                        actorUserId,
                        reference,
                        requestedAmount,
                        payment.Currency,
                        requestedBaseAmount,
                        order.BaseCurrency,
                        occurredAt));
                if (existing == null)
                {
                    _payments.AddRefund(refund);
                }
                else if (existing.Amount != requestedAmount
                    || !string.Equals(
                        existing.Currency,
                        payment.Currency,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        existing.BaseCurrency,
                        order.BaseCurrency,
                        StringComparison.Ordinal))
                {
                    throw new ConflictException(
                        "refund_idempotency_mismatch",
                        "Mã tham chiếu hoàn tiền đã được dùng với nội dung khác.");
                }

                DomainRuleGuard.AsConflict(() =>
                    refund.StartProcessing(
                        occurredAt,
                        occurredAt.AddSeconds(
                            _options.CreationLeaseSeconds)));
                _audit.Write(
                    "payment.refund.requested",
                    nameof(PaymentRefund),
                    refund.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["paymentId"] = payment.Id,
                        ["orderId"] = order.Id,
                        ["amount"] = refund.Amount,
                        ["currency"] = refund.Currency,
                        ["baseAmount"] = refund.BaseAmount,
                        ["baseCurrency"] = refund.BaseCurrency,
                        ["reference"] = refund.IdempotencyKey
                    });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                return BuildPrepared(order, payment, refund, false);
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<OrderResponse> CompleteAsync(
            PreparedRefund prepared,
            Guid actorUserId,
            string? note,
            string providerRefundId,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var order = await _consistency.LockOrderAsync(
                    prepared.OrderId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "order_payment_missing",
                        "Đơn hàng không còn giao dịch thanh toán.");
                var returnRequest = await _fulfillment
                    .LockReturnRequestByOrderIdAsync(
                        order.Id,
                        cancellationToken)
                    ?? throw new ConflictException(
                        "return_request_missing",
                        "Đơn hàng không còn yêu cầu trả hàng.");
                var refund = await _payments.GetRefundAsync(
                    payment.Id,
                    prepared.Reference,
                    tracking: true,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "refund_request_missing",
                        "Không tìm thấy yêu cầu hoàn tiền đã tạo.");

                if (refund.Status != PaymentRefundStatus.Succeeded)
                {
                    var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                    StatusChange<PaymentStatus>? statusChange = null;
                    var expectedRefundedAmount = prepared.RefundedAmountBefore
                        + refund.Amount;
                    if (payment.RefundedAmount == prepared.RefundedAmountBefore)
                    {
                        statusChange = DomainRuleGuard.AsConflict(() =>
                            payment.RecordRefund(refund.Amount, occurredAt));
                    }
                    else if (payment.RefundedAmount != expectedRefundedAmount)
                    {
                        throw new ConflictException(
                            "refund_provider_state_conflict",
                            "Số tiền hoàn từ webhook không khớp với yêu cầu đang hoàn tất.");
                    }

                    DomainRuleGuard.AsConflict(() =>
                        refund.Complete(providerRefundId, occurredAt));
                    if (statusChange is { Changed: true } appliedChange)
                    {
                        _payments.AddStatusHistory(new PaymentStatusHistory
                        {
                            Id = Guid.NewGuid(),
                            PaymentId = payment.Id,
                            ChangedByUserId = actorUserId,
                            FromStatus = appliedChange.Previous,
                            ToStatus = appliedChange.Current,
                            Source = PaymentStatusChangeSource.Gateway,
                            Reference = providerRefundId,
                            OccurredAt = occurredAt,
                            CreatedAt = occurredAt
                        });
                    }

                    if (payment.Status == PaymentStatus.Refunded)
                    {
                        DomainRuleGuard.AsConflict(() =>
                            returnRequest.MarkRefunded(occurredAt));
                        var orderStatus = DomainRuleGuard.AsConflict(() =>
                            order.ChangeStatus(
                                OrderStatus.Refunded,
                                payment.Status));
                        if (orderStatus.Changed)
                        {
                            _orders.AddStatusHistory(new OrderStatusHistory
                            {
                                Id = Guid.NewGuid(),
                                OrderId = order.Id,
                                ChangedByUserId = actorUserId,
                                FromStatus = orderStatus.Previous,
                                ToStatus = orderStatus.Current,
                                Note = NormalizeOptional(note)
                                    ?? $"Hoàn tiền {providerRefundId}",
                                CreatedAt = occurredAt
                            });
                        }
                    }

                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Hoàn tiền đơn hàng",
                        $"Đơn hàng {order.OrderNumber} đã hoàn "
                        + $"{refund.Amount} {refund.Currency}.",
                        order.Id,
                        payment.Id);
                    _audit.Write(
                        "payment.refund.completed",
                        nameof(PaymentRefund),
                        refund.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["paymentId"] = payment.Id,
                            ["orderId"] = order.Id,
                            ["providerRefundId"] = providerRefundId,
                            ["amount"] = refund.Amount,
                            ["currency"] = refund.Currency,
                            ["baseAmount"] = refund.BaseAmount,
                            ["baseCurrency"] = refund.BaseCurrency
                        });
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                completed = true;
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return await _queries.GetByIdAsync(
                prepared.OrderId,
                actorUserId,
                true,
                cancellationToken);
        }

        private Task MarkPendingAsync(
            PreparedRefund prepared,
            Guid actorUserId,
            CancellationToken cancellationToken)
            => UpdateAttemptAsync(
                prepared,
                actorUserId,
                failureCode: null,
                cancellationToken);

        private Task MarkFailedAsync(
            PreparedRefund prepared,
            Guid actorUserId,
            string failureCode,
            CancellationToken cancellationToken)
            => UpdateAttemptAsync(
                prepared,
                actorUserId,
                failureCode,
                cancellationToken);

        private async Task UpdateAttemptAsync(
            PreparedRefund prepared,
            Guid actorUserId,
            string? failureCode,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var refund = await _payments.GetRefundAsync(
                prepared.PaymentId,
                prepared.Reference,
                tracking: true,
                cancellationToken);
            if (refund != null
                && refund.Status != PaymentRefundStatus.Succeeded)
            {
                if (failureCode == null)
                    refund.MarkPending();
                else
                    DomainRuleGuard.AsConflict(() => refund.Fail(failureCode));
                _audit.Write(
                    failureCode == null
                        ? "payment.refund.pending"
                        : "payment.refund.failed",
                    nameof(PaymentRefund),
                    refund.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["paymentId"] = refund.PaymentId,
                        ["failureCode"] = failureCode
                    });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        private static void ValidateRefundState(
            Order order,
            Payment payment,
            ReturnRequest returnRequest)
        {
            if (payment.Method != PaymentMethod.Card
                || string.IsNullOrWhiteSpace(payment.ProviderTransactionId))
            {
                throw new ConflictException(
                    "online_refund_method_unsupported",
                    "Giao dịch không hỗ trợ hoàn tiền trực tuyến.");
            }

            if (order.Status != OrderStatus.Returned
                || returnRequest.Status != ReturnRequestStatus.Received)
            {
                throw new ConflictException(
                    "order_refund_requires_received_return",
                    "Chỉ có thể hoàn tiền sau khi hàng hoàn đã được nhận và kiểm tra.");
            }

            if (payment.Status is not (PaymentStatus.Paid
                or PaymentStatus.PartiallyRefunded))
            {
                throw new ConflictException(
                    "payment_refund_requires_paid",
                    "Chỉ có thể hoàn tiền cho giao dịch đã thanh toán.");
            }
        }

        private static void EnsurePaymentMatchesOrder(
            Order order,
            Payment payment)
        {
            if (!string.Equals(
                    order.Currency,
                    payment.Currency,
                    StringComparison.Ordinal)
                || order.TotalAmount != payment.Amount)
            {
                throw new ConflictException(
                    "payment_order_money_mismatch",
                    "Số tiền hoặc tiền tệ thanh toán không khớp với đơn hàng.");
            }
        }

        private static decimal CalculateBaseRefundAmount(
            Order order,
            Payment payment,
            decimal requestedAmount,
            decimal availableAmount,
            decimal availableBaseAmount)
        {
            if (requestedAmount == availableAmount)
                return new Money(availableBaseAmount, order.BaseCurrency).Amount;

            if (string.Equals(
                payment.Currency,
                order.BaseCurrency,
                StringComparison.Ordinal))
            {
                return new Money(requestedAmount, order.BaseCurrency).Amount;
            }

            if (order.ExchangeRate <= 0)
            {
                throw new DomainRuleViolationException(
                    "refund_exchange_rate_invalid",
                    "Tỷ giá đơn hàng không hợp lệ để hoàn tiền.");
            }

            return Money.Round(
                requestedAmount / order.ExchangeRate,
                order.BaseCurrency).Amount;
        }

        private static PreparedRefund BuildPrepared(
            Order order,
            Payment payment,
            PaymentRefund refund,
            bool alreadyCompleted)
            => new(
                order.Id,
                payment.Id,
                refund.Id,
                payment.ProviderTransactionId!,
                refund.IdempotencyKey,
                refund.Amount,
                refund.Currency,
                payment.RefundedAmount,
                alreadyCompleted);

        private static string NormalizeReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || value.Trim().Length > 200)
            {
                throw new BusinessException(
                    "refund_reference_invalid",
                    "Mã tham chiếu hoàn tiền không hợp lệ.");
            }

            return value.Trim();
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private sealed record PreparedRefund(
            Guid OrderId,
            Guid PaymentId,
            Guid RefundId,
            string ProviderPaymentId,
            string Reference,
            decimal Amount,
            string Currency,
            decimal RefundedAmountBefore,
            bool AlreadyCompleted);
    }
}
