using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;

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
            var type = ParseTransactionType(queryParams.Type);
            DateTime? from = queryParams.From.HasValue
                ? NormalizeUtc(queryParams.From.Value)
                : null;
            DateTime? to = queryParams.To.HasValue
                ? NormalizeUtc(queryParams.To.Value)
                : null;
            if (from.HasValue && to.HasValue && from.Value >= to.Value)
            {
                throw new BusinessException(
                    "inventory_history_range_invalid",
                    "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            }

            var result = await _inventoryRepository.GetTransactionsAsync(
                productId,
                type,
                from,
                to,
                queryParams.ActorUserId,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<InventoryTransactionResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<InventoryProductResponse>> GetProductsAsync(
            InventoryProductQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var result = await _inventoryRepository.GetProductsAsync(
                queryParams,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<InventoryProductResponse>.Create(
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

        private static InventoryTransactionType? ParseTransactionType(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (Enum.TryParse<InventoryTransactionType>(
                    value.Trim(),
                    ignoreCase: true,
                    out var type)
                && Enum.IsDefined(type))
            {
                return type;
            }

            throw new BusinessException(
                "inventory_transaction_type_invalid",
                "Loại giao dịch tồn kho không hợp lệ.");
        }

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
    }
}
