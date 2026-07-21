using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IInventoryService
    {
        Task<PagedResult<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            InventoryQueryParams queryParams,
            CancellationToken cancellationToken = default);

        Task<PagedResult<LowStockProductResponse>> GetLowStockAsync(
            LowStockQueryParams queryParams,
            CancellationToken cancellationToken = default);
    }
}
