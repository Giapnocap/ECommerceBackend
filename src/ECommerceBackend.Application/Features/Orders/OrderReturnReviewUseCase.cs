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
    public sealed class OrderReturnReviewUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly OrderReturnWorkflow _workflow;
        private readonly TimeProvider _timeProvider;

        public OrderReturnReviewUseCase(
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
            ReviewReturnRequest request,
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
                var expectedStatus =
                    request.Decision == ReturnReviewDecision.Approve
                        ? ReturnRequestStatus.Approved
                        : ReturnRequestStatus.Rejected;

                if (returnRequest.Status == expectedStatus)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (returnRequest.Status
                            != ReturnRequestStatus.Pending
                        || order.Status != OrderStatus.ReturnRequested)
                    {
                        throw new ConflictException(
                            "return_review_status_invalid",
                            "Yêu cầu trả hàng không còn ở trạng thái chờ xét duyệt.");
                    }

                    var payment =
                        await _consistency.LockPaymentByOrderIdAsync(
                            order.Id,
                            cancellationToken);
                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    DomainRuleGuard.AsBusiness(() =>
                        returnRequest.Review(
                            request.Decision,
                            actorUserId,
                            occurredAt,
                            request.Note));
                    var nextOrderStatus =
                        request.Decision == ReturnReviewDecision.Approve
                            ? OrderStatus.ReturnApproved
                            : OrderStatus.Delivered;
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            nextOrderStatus,
                            payment?.Status));
                    _workflow.AddHistory(
                        order,
                        statusChange,
                        actorUserId,
                        OrderReturnWorkflow.NormalizeOptional(
                            request.Note),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        request.Decision == ReturnReviewDecision.Approve
                            ? "Yêu cầu trả hàng đã được duyệt"
                            : "Yêu cầu trả hàng bị từ chối",
                        request.Decision == ReturnReviewDecision.Approve
                            ? $"Bạn có thể gửi trả đơn {order.OrderNumber}."
                            : $"Yêu cầu trả đơn {order.OrderNumber} không được chấp nhận.",
                        order.Id,
                        payment?.Id);
                    _workflow.WriteAudit(
                        "return.review",
                        returnRequest,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["decision"] = request.Decision.ToString(),
                            ["note"] =
                                OrderReturnWorkflow.NormalizeOptional(
                                    request.Note)
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
