using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderStatusUpdateUseCase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly OrderCancellationWorkflow _cancellation;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;

        public OrderStatusUpdateUseCase(
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            IAuditWriter audit,
            OrderCancellationWorkflow cancellation,
            OrderQueryUseCase queries,
            TimeProvider timeProvider)
        {
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _audit = audit;
            _cancellation = cancellation;
            _queries = queries;
            _timeProvider = timeProvider;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            UpdateOrderStatusRequest request,
            CancellationToken cancellationToken = default)
        {
            OrderCommandRules.EnsureGenericTransitionIsAllowed(
                request.Status);
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;

            try
            {
                var order = await _consistency.LockOrderAsync(
                    orderId,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy đơn hàng.");

                if (order.Status != request.Status)
                {
                    var payment =
                        await _consistency.LockPaymentByOrderIdAsync(
                            order.Id,
                            cancellationToken);
                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    var statusChange =
                        request.Status == OrderStatus.Cancelled
                            ? DomainRuleGuard.AsBusiness(() =>
                                order.Cancel(
                                    occurredAt,
                                    payment?.Status,
                                    OrderCommandRules.NormalizeOptional(
                                        request.Note)
                                    ?? "CancelledByStaff"))
                            : DomainRuleGuard.AsBusiness(() =>
                                order.ChangeStatus(
                                    request.Status,
                                    payment?.Status));

                    if (request.Status == OrderStatus.Cancelled)
                    {
                        await _cancellation.RestoreStockAsync(
                            order,
                            actorUserId,
                            occurredAt,
                            InventoryTransactionType.OrderCancelled,
                            cancellationToken);
                    }

                    _cancellation.UpdatePayment(
                        payment,
                        request.Status,
                        actorUserId,
                        occurredAt);
                    _cancellation.AddHistory(
                        order,
                        statusChange,
                        actorUserId,
                        request.Note,
                        occurredAt);

                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Cập nhật trạng thái đơn hàng",
                        $"Đơn hàng {order.OrderNumber} đã chuyển sang trạng thái {OrderCommandRules.GetStatusLabel(request.Status)}.",
                        order.Id,
                        payment?.Id);

                    _audit.Write(
                        "order.status.update",
                        nameof(Order),
                        order.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["fromStatus"] =
                                statusChange.Previous.ToString(),
                            ["toStatus"] = request.Status.ToString()
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Đơn hàng hoặc tồn kho vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng hoặc sản phẩm. Vui lòng thử lại.",
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
    }
}
