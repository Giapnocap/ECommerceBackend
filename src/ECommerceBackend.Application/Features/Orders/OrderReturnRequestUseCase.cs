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
    public sealed class OrderReturnRequestUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly OrderReturnWorkflow _workflow;
        private readonly TimeProvider _timeProvider;
        private readonly ReturnPolicyOptions _options;

        public OrderReturnRequestUseCase(
            IFulfillmentRepository fulfillmentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            OrderReturnWorkflow workflow,
            TimeProvider timeProvider,
            IOptions<ReturnPolicyOptions> options)
        {
            _fulfillmentRepository = fulfillmentRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _workflow = workflow;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid customerUserId,
            CreateReturnRequest request,
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
                    cancellationToken);
                if (order == null || order.UserId != customerUserId)
                {
                    throw new NotFoundException(
                        "Không tìm thấy đơn hàng.");
                }

                var existing =
                    await _fulfillmentRepository
                        .LockReturnRequestByOrderIdAsync(
                            orderId,
                            cancellationToken);
                if (existing != null)
                {
                    if (!string.Equals(
                        existing.Reason,
                        request.Reason.Trim(),
                        StringComparison.Ordinal))
                    {
                        throw new ConflictException(
                            "return_request_already_exists",
                            "Đơn hàng đã có một yêu cầu trả hàng khác.");
                    }

                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (order.Status != OrderStatus.Delivered)
                    {
                        throw new ConflictException(
                            "return_request_status_invalid",
                            "Chỉ có thể yêu cầu trả đơn đã giao thành công.");
                    }

                    var shipment =
                        await _fulfillmentRepository
                            .LockShipmentByOrderIdAsync(
                                orderId,
                                cancellationToken);
                    if (shipment?.DeliveredAt == null)
                    {
                        throw new ConflictException(
                            "return_delivery_time_missing",
                            "Đơn hàng chưa có thời điểm giao thành công.");
                    }

                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    if (occurredAt
                        > shipment.DeliveredAt.Value.AddDays(
                            _options.ReturnWindowDays))
                    {
                        throw new ConflictException(
                            "return_window_expired",
                            $"Đơn hàng đã quá thời hạn trả hàng {_options.ReturnWindowDays} ngày.");
                    }

                    var payment =
                        await _consistency.LockPaymentByOrderIdAsync(
                            orderId,
                            cancellationToken)
                        ?? throw new ConflictException(
                            "order_payment_missing",
                            "Đơn hàng không có giao dịch thanh toán.");
                    var returnRequest = DomainRuleGuard.AsBusiness(() =>
                        ReturnRequest.Create(
                            Guid.NewGuid(),
                            order.Id,
                            customerUserId,
                            request.Reason,
                            occurredAt));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.ReturnRequested,
                            payment.Status));
                    _fulfillmentRepository.AddReturnRequest(returnRequest);
                    _workflow.AddHistory(
                        order,
                        statusChange,
                        customerUserId,
                        returnRequest.Reason,
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đã tiếp nhận yêu cầu trả hàng",
                        $"Yêu cầu trả đơn {order.OrderNumber} đang chờ xét duyệt.",
                        order.Id,
                        payment.Id);
                    _workflow.WriteAudit(
                        "return.request",
                        returnRequest,
                        customerUserId,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = returnRequest.Reason
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
                customerUserId,
                false,
                cancellationToken);
        }
    }
}
