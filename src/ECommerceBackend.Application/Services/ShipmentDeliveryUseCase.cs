using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class ShipmentDeliveryUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly OrderFulfillmentWorkflow _workflow;
        private readonly TimeProvider _timeProvider;

        public ShipmentDeliveryUseCase(
            IFulfillmentRepository fulfillmentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            IAuditWriter audit,
            OrderFulfillmentWorkflow workflow,
            TimeProvider timeProvider)
        {
            _fulfillmentRepository = fulfillmentRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _audit = audit;
            _workflow = workflow;
            _timeProvider = timeProvider;
        }

        public async Task<OrderResponse> ExecuteAsync(
            Guid orderId,
            Guid actorUserId,
            MarkShipmentDeliveredRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var completed = false;

            try
            {
                var order = await _workflow.LockOrderAsync(
                    orderId,
                    cancellationToken);
                var shipment =
                    await _fulfillmentRepository.LockShipmentByOrderIdAsync(
                        orderId,
                        cancellationToken)
                    ?? throw new ConflictException(
                        "shipment_missing",
                        "Đơn hàng chưa có vận đơn.");

                if (order.Status == OrderStatus.Delivered
                    && shipment.DeliveredAt.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (order.Status != OrderStatus.Shipping)
                    {
                        throw new ConflictException(
                            "shipment_delivery_status_invalid",
                            "Chỉ có thể xác nhận giao thành công cho đơn đang giao.");
                    }

                    var payment =
                        await _consistency.LockPaymentByOrderIdAsync(
                            order.Id,
                            cancellationToken)
                        ?? throw new ConflictException(
                            "order_payment_missing",
                            "Đơn hàng không có giao dịch thanh toán.");
                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    DomainRuleGuard.AsConflict(() =>
                        shipment.MarkDelivered(occurredAt));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Delivered,
                            payment.Status));
                    _workflow.MarkCodPaymentPaid(
                        payment,
                        order,
                        actorUserId,
                        occurredAt);
                    _workflow.AddOrderHistory(
                        order,
                        statusChange,
                        actorUserId,
                        OrderFulfillmentWorkflow.NormalizeOptional(
                            request.Note),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Giao hàng thành công",
                        $"Đơn hàng {order.OrderNumber} đã được giao thành công.",
                        order.Id,
                        payment.Id);
                    _audit.Write(
                        "shipment.deliver",
                        nameof(Shipment),
                        shipment.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["orderId"] = order.Id,
                            ["deliveredAt"] = occurredAt
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                await OrderFulfillmentWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw new ConflictException(
                    "shipment_concurrency_conflict",
                    "Đơn hàng, vận đơn hoặc thanh toán vừa được cập nhật.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await OrderFulfillmentWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw new ConflictException(
                    "shipment_processing_conflict",
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng.",
                    ex);
            }
            catch
            {
                await OrderFulfillmentWorkflow.RollbackAsync(
                    transaction,
                    completed);
                throw;
            }

            return await _workflow.GetResponseAsync(
                orderId,
                actorUserId,
                cancellationToken);
        }
    }
}
