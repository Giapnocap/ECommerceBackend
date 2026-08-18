using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IInventoryRepository
    {
        Task<bool> ProductExistsAsync(
            Guid productId,
            CancellationToken cancellationToken = default);

        Task<PageSlice<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            InventoryTransactionType? type,
            DateTime? from,
            DateTime? to,
            Guid? actorUserId,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<InventoryProductResponse>> GetProductsAsync(
            InventoryProductQueryParams queryParams,
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
