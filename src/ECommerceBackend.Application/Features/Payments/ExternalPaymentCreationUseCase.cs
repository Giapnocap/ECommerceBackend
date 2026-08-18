using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class ExternalPaymentCreationUseCase
    {
        private readonly IPaymentGateway _gateway;
        private readonly IPaymentRepository _payments;
        private readonly IDataConsistencyService _consistency;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;
        private readonly StripePaymentOptions _options;

        public ExternalPaymentCreationUseCase(
            IPaymentGateway gateway,
            IPaymentRepository payments,
            IDataConsistencyService consistency,
            IUnitOfWork unitOfWork,
            IAuditWriter audit,
            TimeProvider timeProvider,
            IOptions<StripePaymentOptions> options)
        {
            _gateway = gateway;
            _payments = payments;
            _consistency = consistency;
            _unitOfWork = unitOfWork;
            _audit = audit;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<ExternalPaymentResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
        {
            var context = await ClaimAsync(
                orderId,
                actorUserId,
                canProcessOrders,
                cancellationToken);

            GatewayPaymentCreationResult gatewayResult;
            try
            {
                gatewayResult = await _gateway.CreatePaymentAsync(
                    new GatewayPaymentCreationRequest(
                        context.PaymentId,
                        context.OrderId,
                        context.OrderNumber,
                        context.Amount,
                        context.Currency,
                        context.IdempotencyKey),
                    cancellationToken);
            }
            catch
            {
                await ReleaseClaimAsync(
                    context.OrderId,
                    actorUserId,
                    CancellationToken.None);
                throw;
            }

            return await CompleteAsync(
                context,
                actorUserId,
                gatewayResult,
                cancellationToken);
        }

        private async Task<ExternalPaymentContext> ClaimAsync(
            Guid orderId,
            Guid actorUserId,
            bool canProcessOrders,
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
                if (order.UserId != actorUserId && !canProcessOrders)
                {
                    throw new ApiException(
                        403,
                        "payment_access_denied",
                        "Bạn không có quyền khởi tạo thanh toán cho đơn hàng này.");
                }

                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "order_payment_missing",
                        "Đơn hàng không có giao dịch thanh toán.");
                ValidatePayment(order, payment);
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                if (payment.ProviderTransactionId == null)
                {
                    DomainRuleGuard.AsConflict(() =>
                        payment.ClaimExternalCreation(
                            occurredAt,
                            occurredAt.AddSeconds(
                                _options.CreationLeaseSeconds)));
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                completed = true;
                return new ExternalPaymentContext(
                    payment.Id,
                    order.Id,
                    order.OrderNumber,
                    payment.Amount,
                    payment.Currency,
                    payment.ExternalCreationIdempotencyKey!);
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<ExternalPaymentResponse> CompleteAsync(
            ExternalPaymentContext context,
            Guid actorUserId,
            GatewayPaymentCreationResult result,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    context.OrderId,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "order_payment_missing",
                        "Đơn hàng không còn giao dịch thanh toán.");
                var previousStatus = payment.Status;
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var statusChange = DomainRuleGuard.AsConflict(() =>
                    payment.AttachProviderTransaction(
                        _gateway.ProviderCode,
                        result.ProviderPaymentId,
                        result.Status,
                        occurredAt));
                if (statusChange.Changed)
                {
                    _payments.AddStatusHistory(new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ChangedByUserId = actorUserId,
                        FromStatus = previousStatus,
                        ToStatus = payment.Status,
                        Source = PaymentStatusChangeSource.Gateway,
                        Reference = result.ProviderPaymentId,
                        OccurredAt = occurredAt,
                        CreatedAt = occurredAt
                    });
                }

                _audit.Write(
                    "payment.external.created",
                    nameof(Payment),
                    payment.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["orderId"] = payment.OrderId,
                        ["provider"] = payment.Provider,
                        ["providerPaymentId"] = payment.ProviderTransactionId,
                        ["status"] = payment.Status.ToString()
                    });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                return new ExternalPaymentResponse
                {
                    PaymentId = payment.Id,
                    Provider = payment.Provider!,
                    ProviderPaymentId = payment.ProviderTransactionId!,
                    Status = payment.Status.ToString(),
                    ClientSecret = result.ClientSecret
                };
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task ReleaseClaimAsync(
            Guid orderId,
            Guid actorUserId,
            CancellationToken cancellationToken)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var payment = await _consistency.LockPaymentByOrderIdAsync(
                orderId,
                cancellationToken);
            if (payment != null && payment.ProviderTransactionId == null)
            {
                payment.ReleaseExternalCreationClaim();
                _audit.Write(
                    "payment.external.failed",
                    nameof(Payment),
                    payment.Id.ToString(),
                    actorUserId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }

        private static void ValidatePayment(Order order, Payment payment)
        {
            if (payment.Method != PaymentMethod.Card
                || !string.Equals(
                    payment.Provider,
                    "stripe",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    "external_payment_method_unsupported",
                    "Đơn hàng không sử dụng phương thức thanh toán trực tuyến được hỗ trợ.");
            }

            if (order.Status == OrderStatus.Cancelled
                || payment.Status is PaymentStatus.Failed
                    or PaymentStatus.Cancelled
                    or PaymentStatus.Refunded
                    or PaymentStatus.PartiallyRefunded)
            {
                throw new ConflictException(
                    "external_payment_state_invalid",
                    "Đơn hàng hoặc thanh toán không còn ở trạng thái có thể khởi tạo.");
            }

            if (payment.Amount != order.TotalAmount
                || !string.Equals(
                    payment.Currency,
                    order.Currency,
                    StringComparison.Ordinal))
            {
                throw new ConflictException(
                    "payment_order_amount_mismatch",
                    "Số tiền hoặc tiền tệ thanh toán không khớp với đơn hàng.");
            }
        }

        private sealed record ExternalPaymentContext(
            Guid PaymentId,
            Guid OrderId,
            string OrderNumber,
            decimal Amount,
            string Currency,
            string IdempotencyKey);
    }
}
