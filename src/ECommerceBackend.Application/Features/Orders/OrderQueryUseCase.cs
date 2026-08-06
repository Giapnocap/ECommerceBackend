using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderQueryUseCase
    {
        private readonly IOrderRepository _orderRepository;

        public OrderQueryUseCase(IOrderRepository orderRepository)
        {
            _orderRepository = orderRepository;
        }

        public async Task<PagedResult<OrderResponse>> GetMyOrdersAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(page, pageSize);
            var result = await _orderRepository.GetByUserAsync(
                userId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<OrderResponse>.Create(
                result.Items.Select(order => order.ToResponse()),
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<OrderSummaryResponse>> GetMyOrderSummariesAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(page, pageSize);
            var result = await _orderRepository.GetSummariesByUserAsync(
                userId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<OrderSummaryResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<OrderResponse> GetByIdAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
        {
            var order = await _orderRepository.GetByIdAsync(
                orderId,
                canProcessOrders ? null : userId,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
            return order.ToResponse();
        }

        public async Task<PagedResult<OrderResponse>> GetAllOrdersAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                queryParams.Page,
                queryParams.PageSize);
            var result = await _orderRepository.GetAllAsync(
                queryParams.Status,
                queryParams.UserId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<OrderResponse>.Create(
                result.Items.Select(order => order.ToResponse()),
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<OrderSummaryResponse>> GetOrderSummariesAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                queryParams.Page,
                queryParams.PageSize);
            var result = await _orderRepository.GetSummariesAsync(
                queryParams.Status,
                queryParams.UserId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<OrderSummaryResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }
    }
}
