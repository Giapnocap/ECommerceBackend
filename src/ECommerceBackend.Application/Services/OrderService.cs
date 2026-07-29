using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderService : IOrderService
    {
        private readonly OrderCheckoutUseCase _checkout;
        private readonly OrderStatusUpdateUseCase _statusUpdate;
        private readonly CustomerOrderCancellationUseCase
            _customerCancellation;
        private readonly PendingOrderExpirationUseCase _expiration;
        private readonly OrderRefundUseCase _refund;
        private readonly ShipmentDispatchUseCase _shipmentDispatch;
        private readonly ShipmentDeliveryUseCase _shipmentDelivery;
        private readonly OrderReturnRequestUseCase _returnRequest;
        private readonly OrderReturnReviewUseCase _returnReview;
        private readonly OrderReturnReceiptUseCase _returnReceipt;
        private readonly OrderQueryUseCase _queries;
        private readonly OrderPricingUseCase _pricing;

        public OrderService(
            OrderCheckoutUseCase checkout,
            OrderStatusUpdateUseCase statusUpdate,
            CustomerOrderCancellationUseCase customerCancellation,
            PendingOrderExpirationUseCase expiration,
            OrderRefundUseCase refund,
            ShipmentDispatchUseCase shipmentDispatch,
            ShipmentDeliveryUseCase shipmentDelivery,
            OrderReturnRequestUseCase returnRequest,
            OrderReturnReviewUseCase returnReview,
            OrderReturnReceiptUseCase returnReceipt,
            OrderQueryUseCase queries,
            OrderPricingUseCase pricing)
        {
            _checkout = checkout;
            _statusUpdate = statusUpdate;
            _customerCancellation = customerCancellation;
            _expiration = expiration;
            _refund = refund;
            _shipmentDispatch = shipmentDispatch;
            _shipmentDelivery = shipmentDelivery;
            _returnRequest = returnRequest;
            _returnReview = returnReview;
            _returnReceipt = returnReceipt;
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
            => _statusUpdate.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> CancelByCustomerAsync(
            Guid orderId,
            Guid customerUserId,
            CancelOrderRequest request,
            CancellationToken cancellationToken = default)
            => _customerCancellation.ExecuteAsync(
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

        public Task<OrderResponse> DispatchShipmentAsync(
            Guid orderId,
            Guid actorUserId,
            DispatchShipmentRequest request,
            CancellationToken cancellationToken = default)
            => _shipmentDispatch.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> MarkShipmentDeliveredAsync(
            Guid orderId,
            Guid actorUserId,
            MarkShipmentDeliveredRequest request,
            CancellationToken cancellationToken = default)
            => _shipmentDelivery.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> RequestReturnAsync(
            Guid orderId,
            Guid customerUserId,
            CreateReturnRequest request,
            CancellationToken cancellationToken = default)
            => _returnRequest.ExecuteAsync(
                orderId,
                customerUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> ReviewReturnAsync(
            Guid orderId,
            Guid actorUserId,
            ReviewReturnRequest request,
            CancellationToken cancellationToken = default)
            => _returnReview.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<OrderResponse> ReceiveReturnAsync(
            Guid orderId,
            Guid actorUserId,
            ReceiveReturnRequest request,
            CancellationToken cancellationToken = default)
            => _returnReceipt.ExecuteAsync(
                orderId,
                actorUserId,
                request,
                cancellationToken);

        public Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
            => _expiration.GetDueOrderIdsAsync(
                asOf,
                batchSize,
                cancellationToken);

        public Task<bool> ExpirePendingOrderAsync(
            Guid orderId,
            DateTime asOf,
            CancellationToken cancellationToken = default)
            => _expiration.ExecuteAsync(
                orderId,
                asOf,
                cancellationToken);
    }
}
