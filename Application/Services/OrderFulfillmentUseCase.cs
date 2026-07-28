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

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderFulfillmentUseCase
    {
        private readonly IFulfillmentRepository _fulfillmentRepository;
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly OrderQueryUseCase _queries;
        private readonly TimeProvider _timeProvider;

        public OrderFulfillmentUseCase(
            IFulfillmentRepository fulfillmentRepository,
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            IAuditWriter audit,
            OrderQueryUseCase queries,
            TimeProvider timeProvider)
        {
            _fulfillmentRepository = fulfillmentRepository;
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _audit = audit;
            _queries = queries;
            _timeProvider = timeProvider;
        }

        public async Task<OrderResponse> DispatchAsync(
            Guid orderId,
            Guid actorUserId,
            DispatchShipmentRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var order = await LockOrderAsync(orderId, cancellationToken);
                var shipment =
                    await _fulfillmentRepository.LockShipmentByOrderIdAsync(
                        orderId,
                        cancellationToken);

                if (order.Status == OrderStatus.Shipping)
                {
                    EnsureShipmentMatches(shipment, request);
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

                    var occurredAt = UtcNow;
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
                        EnsureShipmentMatches(shipment, request);
                    }

                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Shipping,
                            order.Payment?.Status));
                    AddOrderHistory(
                        order,
                        statusChange,
                        actorUserId,
                        NormalizeOptional(request.Note)
                            ?? $"Xuất giao qua {shipment.Carrier}",
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Đơn hàng đang được giao",
                        $"Đơn hàng {order.OrderNumber} đã được giao cho {shipment.Carrier}. Mã vận đơn: {shipment.TrackingNumber}.",
                        order.Id);
                    _audit.Write(
                        "shipment.dispatch",
                        "Shipment",
                        shipment.Id.ToString(),
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["orderId"] = order.Id,
                            ["carrier"] = shipment.Carrier,
                            ["trackingNumber"] = shipment.TrackingNumber
                        });

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                }
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await RollbackAsync(transaction, completed);
                throw new ConflictException(
                    "shipment_concurrency_conflict",
                    "Đơn hàng hoặc vận đơn vừa được cập nhật. Vui lòng tải lại và thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await RollbackAsync(transaction, completed);
                throw new ConflictException(
                    "shipment_processing_conflict",
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng.",
                    ex);
            }
            catch
            {
                await RollbackAsync(transaction, completed);
                throw;
            }

            return await GetResponseAsync(orderId, actorUserId, cancellationToken);
        }

        public async Task<OrderResponse> MarkDeliveredAsync(
            Guid orderId,
            Guid actorUserId,
            MarkShipmentDeliveredRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;
            try
            {
                var order = await LockOrderAsync(orderId, cancellationToken);
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

                    var payment = await _consistency.LockPaymentByOrderIdAsync(
                        order.Id,
                        cancellationToken)
                        ?? throw new ConflictException(
                            "order_payment_missing",
                            "Đơn hàng không có giao dịch thanh toán.");
                    var occurredAt = UtcNow;
                    DomainRuleGuard.AsConflict(() =>
                        shipment.MarkDelivered(occurredAt));
                    var statusChange = DomainRuleGuard.AsConflict(() =>
                        order.ChangeStatus(
                            OrderStatus.Delivered,
                            payment.Status));
                    MarkCodPaymentPaid(
                        payment,
                        order,
                        actorUserId,
                        occurredAt);
                    AddOrderHistory(
                        order,
                        statusChange,
                        actorUserId,
                        NormalizeOptional(request.Note),
                        occurredAt);
                    _outbox.EnqueueNotification(
                        order.UserId,
                        "Giao hàng thành công",
                        $"Đơn hàng {order.OrderNumber} đã được giao thành công.",
                        order.Id,
                        payment.Id);
                    _audit.Write(
                        "shipment.deliver",
                        "Shipment",
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
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await RollbackAsync(transaction, completed);
                throw new ConflictException(
                    "shipment_concurrency_conflict",
                    "Đơn hàng, vận đơn hoặc thanh toán vừa được cập nhật.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await RollbackAsync(transaction, completed);
                throw new ConflictException(
                    "shipment_processing_conflict",
                    "Hệ thống đang xử lý giao dịch khác trên cùng đơn hàng.",
                    ex);
            }
            catch
            {
                await RollbackAsync(transaction, completed);
                throw;
            }

            return await GetResponseAsync(orderId, actorUserId, cancellationToken);
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        private async Task<Order> LockOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken)
            => await _consistency.LockOrderAsync(orderId, cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");

        private void MarkCodPaymentPaid(
            Payment payment,
            Order order,
            Guid actorUserId,
            DateTime occurredAt)
        {
            if (payment.Method != PaymentMethod.CashOnDelivery
                || payment.Status != PaymentStatus.Pending)
            {
                return;
            }

            var paymentChange = DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(PaymentStatus.Paid, occurredAt));
            _paymentRepository.AddStatusHistory(new PaymentStatusHistory
            {
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                ChangedByUserId = actorUserId,
                FromStatus = paymentChange.Previous,
                ToStatus = PaymentStatus.Paid,
                Source = PaymentStatusChangeSource.OrderLifecycle,
                Reference = order.OrderNumber,
                OccurredAt = occurredAt,
                CreatedAt = occurredAt
            });
        }

        private void AddOrderHistory(
            Order order,
            StatusChange<OrderStatus> statusChange,
            Guid actorUserId,
            string? note,
            DateTime occurredAt)
            => _orderRepository.AddStatusHistory(new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                ChangedByUserId = actorUserId,
                FromStatus = statusChange.Previous,
                ToStatus = statusChange.Current,
                Note = note,
                CreatedAt = occurredAt
            });

        private static void EnsureShipmentMatches(
            Shipment? shipment,
            DispatchShipmentRequest request)
        {
            if (shipment == null
                || !shipment.Matches(request.Carrier, request.TrackingNumber))
            {
                throw new ConflictException(
                    "shipment_identity_mismatch",
                    "Đơn hàng đã được gắn với một vận đơn khác.");
            }
        }

        private Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid actorUserId,
            CancellationToken cancellationToken)
            => _queries.GetByIdAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);

        private static async Task RollbackAsync(
            IAppTransaction transaction,
            bool completed)
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
