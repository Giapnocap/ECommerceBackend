using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderReturnReceiptUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly OrderReturnWorkflow _workflow;
        private readonly TimeProvider _timeProvider;

        public OrderReturnReceiptUseCase(
            IFulfillmentRepository fulfillmentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderReturnWorkflow workflow,
            TimeProvider timeProvider)
        {
            _fulfillmentRepository = fulfillmentRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _workflow = workflow;
            _timeProvider = timeProvider;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            ReceiveReturnRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var completed = false;

            try
            {
                var order = await _consistency.LockOrderAsync(
                    orderId,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy đơn hàng.");
                var returnRequest =
                    await _fulfillmentRepository
                        .LockReturnRequestByOrderIdAsync(
                            orderId,
                            cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy yêu cầu trả hàng.");

                if ((returnRequest.Status is ReturnRequestStatus.Received
                    or ReturnRequestStatus.Refunded)
                    && (order.Status is OrderStatus.Returned
                        or OrderStatus.Refunded))
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (returnRequest.Status
                            != ReturnRequestStatus.Approved
                        || order.Status != OrderStatus.ReturnApproved)
                    {
                        throw new ConflictException(
                            "return_receive_status_invalid",
                            "Chỉ có thể nhận hàng hoàn của yêu cầu đã được duyệt.");
                    }

                    var payment =
                        await _consistency.LockPaymentByOrderIdAsync(
                            order.Id,
                            cancellationToken);
                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    DomainRuleGuard.AsBusiness(() =>
                        returnRequest.Receive(
                            actorUserId,
                            occurredAt,
                            request.InspectionNote));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Returned,
                            payment?.Status));
                    await _workflow.RestoreStockAsync(
                        order,
                        actorUserId,
                        occurredAt,
                        cancellationToken);
                    _workflow.AddHistory(
                        order,
                        statusChange,
                        actorUserId,
                        request.InspectionNote.Trim(),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đã nhận hàng hoàn",
                        $"Đơn hàng {order.OrderNumber} đã được nhận lại và đang chờ hoàn tiền.",
                        order.Id,
                        payment?.Id);
                    _workflow.WriteAudit(
                        "return.receive",
                        returnRequest,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["inspectionNote"] =
                                request.InspectionNote.Trim()
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                await OrderReturnWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw OrderReturnWorkflow.ProcessingConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await OrderReturnWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw OrderReturnWorkflow.ProcessingConflict(ex);
            }
            catch
            {
                await OrderReturnWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw;
            }

            return await _workflow.GetResponseAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);
        }
    }
}
