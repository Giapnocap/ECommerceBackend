using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
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
            InventoryTransactionType? type,
            DateTime? from,
            DateTime? to,
            Guid? actorUserId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.InventoryTransactions
                .AsNoTracking()
                .Where(transaction => transaction.ProductId == productId);
            if (type.HasValue)
                query = query.Where(transaction => transaction.Type == type.Value);
            if (from.HasValue)
                query = query.Where(transaction => transaction.CreatedAt >= from.Value);
            if (to.HasValue)
                query = query.Where(transaction => transaction.CreatedAt < to.Value);
            if (actorUserId.HasValue)
            {
                query = query.Where(
                    transaction => transaction.CreatedByUserId == actorUserId.Value);
            }

            query = query
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
                    BeforeQuantity = transaction.BalanceAfter - transaction.QuantityChange,
                    QuantityChange = transaction.QuantityChange,
                    BalanceAfter = transaction.BalanceAfter,
                    Reference = transaction.Reference,
                    Reason = transaction.Reason,
                    CreatedAt = transaction.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return new PageSlice<InventoryTransactionResponse>(items, totalCount);
        }

        public async Task<PageSlice<InventoryProductResponse>> GetProductsAsync(
            InventoryProductQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Products
                .AsNoTracking()
                .Where(product => !product.IsDeleted
                    && product.Category != null
                    && !product.Category.IsDeleted);
            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                query = query.Where(product => product.Name.Contains(keyword));
            }

            if (queryParams.CategoryId.HasValue)
            {
                query = query.Where(
                    product => product.CategoryId == queryParams.CategoryId.Value);
            }

            if (queryParams.LowStockOnly)
            {
                query = queryParams.LowStockThreshold.HasValue
                    ? query.Where(product =>
                        product.StockQuantity <= queryParams.LowStockThreshold.Value)
                    : query.Where(product =>
                        product.StockQuantity <= product.LowStockThreshold);
            }

            query = ApplyProductSort(query, queryParams);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(skip)
                .Take(take)
                .Select(product => new InventoryProductResponse
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    CategoryId = product.CategoryId,
                    CategoryName = product.Category!.Name,
                    StockQuantity = product.StockQuantity,
                    LowStockThreshold = product.LowStockThreshold,
                    IsLowStock = product.StockQuantity <=
                        (queryParams.LowStockThreshold ?? product.LowStockThreshold),
                    CreatedAt = product.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return new PageSlice<InventoryProductResponse>(items, totalCount);
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

        private static IQueryable<Product> ApplyProductSort(
            IQueryable<Product> query,
            InventoryProductQueryParams queryParams)
            => (queryParams.SortBy?.ToLowerInvariant(), queryParams.SortOrder?.ToLowerInvariant()) switch
            {
                ("name", "desc") => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
                ("name", _) => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
                ("createdat", "asc") => query.OrderBy(product => product.CreatedAt).ThenBy(product => product.Id),
                ("createdat", _) => query.OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id),
                ("stock", "desc") => query.OrderByDescending(product => product.StockQuantity).ThenBy(product => product.Id),
                _ => query.OrderBy(product => product.StockQuantity).ThenBy(product => product.Id)
            };
    }
}
