using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderFulfillmentWorkflow
    {
        private readonly IOrderRepository _orderRepository;
        private readonly IPaymentRepository _paymentRepository;
        private readonly IDataConsistencyService _consistency;
        private readonly OrderQueryUseCase _queries;

        public OrderFulfillmentWorkflow(
            IOrderRepository orderRepository,
            IPaymentRepository paymentRepository,
            IDataConsistencyService consistency,
            OrderQueryUseCase queries)
        {
            _orderRepository = orderRepository;
            _paymentRepository = paymentRepository;
            _consistency = consistency;
            _queries = queries;
        }

        internal async Task<Order> LockOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken)
            => await _consistency.LockOrderAsync(
                orderId,
                cancellationToken)
                ?? throw new NotFoundException(
                    "Không tìm thấy đơn hàng.");

        internal void MarkCodPaymentPaid(
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
            _paymentRepository.AddStatusHistory(
                new PaymentStatusHistory
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

        internal void AddOrderHistory(
            Order order,
            StatusChange<OrderStatus> statusChange,
            Guid actorUserId,
            string? note,
            DateTime occurredAt)
        {
            _orderRepository.AddStatusHistory(
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    ChangedByUserId = actorUserId,
                    FromStatus = statusChange.Previous,
                    ToStatus = statusChange.Current,
                    Note = note,
                    CreatedAt = occurredAt
                });
        }

        internal Task<OrderResponse> GetResponseAsync(
            Guid orderId,
            Guid actorUserId,
            CancellationToken cancellationToken)
            => _queries.GetByIdAsync(
                orderId,
                actorUserId,
                true,
                cancellationToken);

        internal static void EnsureShipmentMatches(
            Shipment? shipment,
            DispatchShipmentRequest request)
        {
            if (shipment == null
                || !shipment.Matches(
                    request.Carrier,
                    request.TrackingNumber))
            {
                throw new ConflictException(
                    "shipment_identity_mismatch",
                    "Đơn hàng đã được gắn với một vận đơn khác.");
            }
        }

        internal static async Task RollbackAsync(
            IAppTransaction transaction,
            bool completed)
        {
            if (!completed)
                await transaction.RollbackAsync(CancellationToken.None);
        }

        internal static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
