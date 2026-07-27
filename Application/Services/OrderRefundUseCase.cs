using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderRefundUseCase
    {
        private readonly IPaymentRepository _paymentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;

        public OrderRefundUseCase(
            IPaymentRepository paymentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderQueryUseCase queries)
            : this(
                paymentRepository,
                unitOfWork,
                consistency,
                outbox,
                queries,
                TimeProvider.System)
        {
        }

        public OrderRefundUseCase(
            IPaymentRepository paymentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderQueryUseCase queries,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _paymentRepository = paymentRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _queries = queries;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            RecordOrderRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(orderId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
                var payment = await _consistency.LockPaymentByOrderIdAsync(
                    order.Id,
                    cancellationToken)
                    ?? throw new ConflictException(
                        "order_payment_missing",
                        "Đơn hàng không có giao dịch thanh toán để hoàn tiền.");

                if (order.Status != OrderStatus.Returned)
                {
                    throw new ConflictException(
                        "order_refund_requires_returned",
                        "Chỉ có thể ghi nhận hoàn tiền sau khi đơn hàng đã được hoàn.");
                }

                if (payment.Method != PaymentMethod.CashOnDelivery)
                {
                    throw new ConflictException(
                        "manual_refund_method_unsupported",
                        "Luồng ghi nhận hoàn tiền thủ công chỉ hỗ trợ thanh toán khi nhận hàng.");
                }

                var reference = NormalizeReference(request.Reference);
                if (payment.Status == PaymentStatus.Refunded)
                {
                    var existingRefund =
                        await _paymentRepository.GetRefundHistoryAsync(
                            payment.Id,
                            cancellationToken);
                    if (existingRefund == null
                        || existingRefund.Source != PaymentStatusChangeSource.ManualRefund
                        || !string.Equals(
                            existingRefund.Reference,
                            reference,
                            StringComparison.Ordinal))
                    {
                        throw new ConflictException(
                            "refund_reference_mismatch",
                            "Giao dịch đã được hoàn tiền với một mã tham chiếu khác.");
                    }
                }

                if (payment.Status != PaymentStatus.Refunded)
                {
                    if (payment.Status != PaymentStatus.Paid)
                    {
                        throw new ConflictException(
                            "payment_refund_requires_paid",
                            "Chỉ có thể hoàn tiền cho giao dịch đã thanh toán.");
                    }

                    var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        payment.ChangeStatus(PaymentStatus.Refunded, occurredAt));

                    _paymentRepository.AddStatusHistory(new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ChangedByUserId = actorUserId,
                        FromStatus = statusChange.Previous,
                        ToStatus = PaymentStatus.Refunded,
                        Source = PaymentStatusChangeSource.ManualRefund,
                        Reference = reference,
                        OccurredAt = occurredAt,
                        CreatedAt = occurredAt
                    });

                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Hoàn tiền đơn hàng",
                        $"Đơn hàng {order.OrderNumber} đã được ghi nhận hoàn tiền.",
                        order.Id,
                        payment.Id);

                    _audit.Write(
                        "payment.refund.record",
                        "Payment",
                        payment.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["orderId"] = order.Id,
                            ["reference"] = reference,
                            ["note"] = NormalizeOptional(request.Note)
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Đơn hàng hoặc thanh toán vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await _queries.GetByIdAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);
        }

        private static string NormalizeReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BusinessException(
                    "refund_reference_required",
                    "Mã tham chiếu hoàn tiền không được để trống.");
            }

            var normalized = value.Trim();
            if (normalized.Length > 200)
            {
                throw new BusinessException(
                    "refund_reference_too_long",
                    "Mã tham chiếu hoàn tiền không được vượt quá 200 ký tự.");
            }

            return normalized;
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
