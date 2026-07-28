using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IOrderService
    {
        Task<OrderResponse> PlaceOrderAsync(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            CancellationToken cancellationToken = default);

        Task<OrderQuoteResponse> GetQuoteAsync(
            Guid userId,
            OrderQuoteRequest request,
            CancellationToken cancellationToken = default);

        Task<PagedResult<OrderResponse>> GetMyOrdersAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> GetByIdAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default);

        Task<PagedResult<OrderResponse>> GetAllOrdersAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> UpdateStatusAsync(
            Guid orderId,
            Guid actorUserId,
            UpdateOrderStatusRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> CancelByCustomerAsync(
            Guid orderId,
            Guid customerUserId,
            CancelOrderRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> DispatchShipmentAsync(
            Guid orderId,
            Guid actorUserId,
            DispatchShipmentRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> MarkShipmentDeliveredAsync(
            Guid orderId,
            Guid actorUserId,
            MarkShipmentDeliveredRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> RequestReturnAsync(
            Guid orderId,
            Guid customerUserId,
            CreateReturnRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> ReviewReturnAsync(
            Guid orderId,
            Guid actorUserId,
            ReviewReturnRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> ReceiveReturnAsync(
            Guid orderId,
            Guid actorUserId,
            ReceiveReturnRequest request,
            CancellationToken cancellationToken = default);

        Task<OrderResponse> RecordRefundAsync(
            Guid orderId,
            Guid actorUserId,
            RecordOrderRefundRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default);

        Task<bool> ExpirePendingOrderAsync(
            Guid orderId,
            DateTime asOf,
            CancellationToken cancellationToken = default);
    }
}
