using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderService : IOrderService
    {
        private readonly OrderCheckoutUseCase _checkout;
        private readonly OrderCommandService _commands;
        private readonly OrderRefundUseCase _refund;
        private readonly OrderQueryUseCase _queries;
        private readonly OrderPricingUseCase _pricing;

        public OrderService(
            OrderCheckoutUseCase checkout,
            OrderCommandService commands,
            OrderRefundUseCase refund,
            OrderQueryUseCase queries,
            OrderPricingUseCase pricing)
        {
            _checkout = checkout;
            _commands = commands;
            _refund = refund;
            _queries = queries;
            _pricing = pricing;
        }

        public Task<OrderResponse> PlaceOrderAsync(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => _checkout.ExecuteAsync(
                userId,
                request,
                idempotencyKey,
                cancellationToken);

        public Task<OrderQuoteResponse> GetQuoteAsync(
            Guid userId,
            OrderQuoteRequest request,
            CancellationToken cancellationToken = default)
            => _pricing.GetQuoteAsync(
                userId,
                request,
                cancellationToken);

        public Task<PagedResult<OrderResponse>> GetMyOrdersAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
            => _queries.GetMyOrdersAsync(
                userId,
                page,
                pageSize,
                cancellationToken);

        public Task<OrderResponse> GetByIdAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
            => _queries.GetByIdAsync(
                orderId,
                userId,
                canProcessOrders,
                cancellationToken);

        public Task<PagedResult<OrderResponse>> GetAllOrdersAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default)
            => _queries.GetAllOrdersAsync(queryParams, cancellationToken);

        public Task<OrderResponse> UpdateStatusAsync(
            Guid orderId,
            Guid actorUserId,
            UpdateOrderStatusRequest request,
            CancellationToken cancellationToken = default)
            => _commands.UpdateStatusAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> CancelByCustomerAsync(
            Guid orderId,
            Guid customerUserId,
            CancelOrderRequest request,
            CancellationToken cancellationToken = default)
            => _commands.CancelByCustomerAsync(
                orderId,
                customerUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> RecordRefundAsync(
            Guid orderId,
            Guid actorUserId,
            RecordOrderRefundRequest request,
            CancellationToken cancellationToken = default)
            => _refund.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
            => _commands.GetDuePendingOrderIdsAsync(
                asOf,
                batchSize,
                cancellationToken);

        public Task<bool> ExpirePendingOrderAsync(
            Guid orderId,
            DateTime asOf,
            CancellationToken cancellationToken = default)
            => _commands.ExpirePendingOrderAsync(
                orderId,
                asOf,
                cancellationToken);
    }
}
