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
    public sealed class ShipmentDispatchUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly OrderFulfillmentWorkflow _workflow;
        private readonly TimeProvider _timeProvider;

        public ShipmentDispatchUseCase(
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
            DispatchShipmentRequest request,
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
                        cancellationToken);

                if (order.Status == OrderStatus.Shipping)
                {
                    OrderFulfillmentWorkflow.EnsureShipmentMatches(
                        shipment,
                        request);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
                else
                {
                    if (order.Status is not (OrderStatus.Confirmed
                        or OrderStatus.DeliveryFailed))
                    {
                        throw new ConflictException(
                            "shipment_dispatch_status_invalid",
                            "Chỉ có thể xuất giao đơn đã xác nhận hoặc giao thất bại.");
                    }

                    var occurredAt =
                        _timeProvider.GetUtcNow().UtcDateTime;
                    if (shipment == null)
                    {
                        shipment = DomainRuleGuard.AsBusiness(() =>
                            Shipment.Create(
                                Guid.NewGuid(),
                                order.Id,
                                request.Carrier,
                                request.TrackingNumber,
                                actorUserId,
                                occurredAt));
                        _fulfillmentRepository.AddShipment(shipment);
                    }
                    else
                    {
                        OrderFulfillmentWorkflow.EnsureShipmentMatches(
                            shipment,
                            request);
                    }

                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Shipping,
                            order.Payment?.Status));
                    _workflow.AddOrderHistory(
                        order,
                        statusChange,
                        actorUserId,
                        OrderFulfillmentWorkflow.NormalizeOptional(
                            request.Note)
                        ?? $"Xuất giao qua {shipment.Carrier}",
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đơn hàng đang được giao",
                        $"Đơn hàng {order.OrderNumber} đã được giao cho {shipment.Carrier}. Mã vận đơn: {shipment.TrackingNumber}.",
                        order.Id);
                    _audit.Write(
                        "shipment.dispatch",
                        nameof(Shipment),
                        shipment.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["orderId"] = order.Id,
                            ["carrier"] = shipment.Carrier,
                            ["trackingNumber"] =
                                shipment.TrackingNumber
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
                    "Đơn hàng hoặc vận đơn vừa được cập nhật. Vui lòng tải lại và thử lại.",
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
