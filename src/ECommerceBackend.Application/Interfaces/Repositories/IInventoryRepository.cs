using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IInventoryRepository
    {
        Task<bool> ProductExistsAsync(
            Guid productId,
            CancellationToken cancellationToken = default);

        Task<PageSlice<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<LowStockProductResponse>> GetLowStockAsync(
            int threshold,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        void AddTransaction(InventoryTransaction transaction);
    }
}
