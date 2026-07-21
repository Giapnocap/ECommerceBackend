using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class InventoryService : IInventoryService
    {
        private readonly IAppDbContext _context;

        public InventoryService(IAppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            InventoryQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            if (!await _context.Products.AsNoTracking()
                .AnyAsync(product => product.Id == productId, cancellationToken))
            {
                throw new NotFoundException("Không tìm thấy sản phẩm.");
            }

            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var query = _context.InventoryTransactions
                .AsNoTracking()
                .Where(transaction => transaction.ProductId == productId)
                .OrderByDescending(transaction => transaction.CreatedAt)
                .ThenByDescending(transaction => transaction.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(transaction => new InventoryTransactionResponse
                {
                    Id = transaction.Id,
                    ProductId = transaction.ProductId,
                    ProductName = transaction.Product != null ? transaction.Product.Name : string.Empty,
                    OrderId = transaction.OrderId,
                    CreatedByUserId = transaction.CreatedByUserId,
                    Type = transaction.Type.ToString(),
                    QuantityChange = transaction.QuantityChange,
                    BalanceAfter = transaction.BalanceAfter,
                    Reason = transaction.Reason,
                    CreatedAt = transaction.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return PagedResult<InventoryTransactionResponse>.Create(
                items,
                totalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<PagedResult<LowStockProductResponse>> GetLowStockAsync(
            LowStockQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            var query = _context.Products
                .AsNoTracking()
                .Where(product => !product.IsDeleted
                    && product.Category != null
                    && !product.Category.IsDeleted
                    && product.StockQuantity <= queryParams.Threshold)
                .OrderBy(product => product.StockQuantity)
                .ThenBy(product => product.Name)
                .ThenBy(product => product.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(product => new LowStockProductResponse
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    CategoryName = product.Category != null ? product.Category.Name : string.Empty,
                    StockQuantity = product.StockQuantity
                })
                .ToListAsync(cancellationToken);

            return PagedResult<LowStockProductResponse>.Create(
                items,
                totalCount,
                paging.Page,
                paging.Size);
        }
    }
}
