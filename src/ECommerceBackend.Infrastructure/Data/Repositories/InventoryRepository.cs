using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class InventoryRepository : IInventoryRepository
    {
        private readonly AppDbContext _context;

        public InventoryRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> ProductExistsAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
            => _context.Products
                .AsNoTracking()
                .AnyAsync(product => product.Id == productId, cancellationToken);

        public async Task<PageSlice<InventoryTransactionResponse>> GetTransactionsAsync(
            Guid productId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.InventoryTransactions
                .AsNoTracking()
                .Where(transaction => transaction.ProductId == productId)
                .OrderByDescending(transaction => transaction.CreatedAt)
                .ThenByDescending(transaction => transaction.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(skip)
                .Take(take)
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

            return new PageSlice<InventoryTransactionResponse>(items, totalCount);
        }

        public async Task<PageSlice<LowStockProductResponse>> GetLowStockAsync(
            int threshold,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Products
                .AsNoTracking()
                .Where(product => !product.IsDeleted
                    && product.Category != null
                    && !product.Category.IsDeleted
                    && product.StockQuantity <= threshold)
                .OrderBy(product => product.StockQuantity)
                .ThenBy(product => product.Name)
                .ThenBy(product => product.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(skip)
                .Take(take)
                .Select(product => new LowStockProductResponse
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    CategoryName = product.Category != null ? product.Category.Name : string.Empty,
                    StockQuantity = product.StockQuantity
                })
                .ToListAsync(cancellationToken);

            return new PageSlice<LowStockProductResponse>(items, totalCount);
        }

        public void AddTransaction(InventoryTransaction transaction)
            => _context.InventoryTransactions.Add(transaction);
    }
}
