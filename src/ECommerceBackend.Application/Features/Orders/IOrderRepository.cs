using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IOrderRepository
    {
        Task<PageSlice<Order>> GetByUserAsync(
            Guid userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<OrderSummaryResponse>> GetSummariesByUserAsync(
            Guid userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<Order?> GetByIdAsync(
            Guid orderId,
            Guid? ownerUserId,
            CancellationToken cancellationToken = default);

        Task<PageSlice<Order>> GetAllAsync(
            OrderStatus? status,
            Guid? userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<OrderSummaryResponse>> GetSummariesAsync(
            OrderStatus? status,
            Guid? userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<Order?> FindByIdempotencyKeyAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken = default);

        Task<int> CountPendingByUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default);

        Task LoadDetailsAsync(
            Order order,
            CancellationToken cancellationToken = default);

        void Add(Order order);

        void AddDetail(OrderDetail detail);

        void AddStatusHistory(OrderStatusHistory history);
    }
}
