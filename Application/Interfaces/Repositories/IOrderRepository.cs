using ECommerceBackend.Application.Common;
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
    }
}
