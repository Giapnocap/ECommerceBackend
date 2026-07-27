using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;

namespace ECommerceBackend.Application.Services
{
    public sealed class InventoryService : IInventoryService
    {
        private readonly IInventoryRepository _inventoryRepository;

        public InventoryService(IInventoryRepository inventoryRepository)
        {
            _inventoryRepository = inventoryRepository;
        }

        public async Task<PagedResult<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            InventoryQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            if (!await _inventoryRepository.ProductExistsAsync(
                productId,
                cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy sản phẩm.");
            }

            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var result = await _inventoryRepository.GetTransactionsAsync(
                productId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<InventoryTransactionResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<LowStockProductResponse>> GetLowStockAsync(
            LowStockQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var result = await _inventoryRepository.GetLowStockAsync(
                queryParams.Threshold,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<LowStockProductResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }
    }
}
