using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class ProductRepository : IProductRepository
    {
        private readonly AppDbContext _context;

        public ProductRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PageSlice<Product>> GetPageAsync(
            ProductQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Products
                .AsNoTracking()
                .Where(product => !product.IsDeleted
                    && product.Category != null
                    && !product.Category.IsDeleted)
                .Include(product => product.Category)
                .Include(product => product.Images)
                .AsSplitQuery()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                query = query.Where(product => product.Name.Contains(keyword)
                    || product.Description.Contains(keyword));
            }

            if (queryParams.CategoryId.HasValue)
                query = query.Where(product => product.CategoryId == queryParams.CategoryId.Value);
            if (queryParams.MinPrice.HasValue)
                query = query.Where(product => product.Price >= queryParams.MinPrice.Value);
            if (queryParams.MaxPrice.HasValue)
                query = query.Where(product => product.Price <= queryParams.MaxPrice.Value);

            query = (queryParams.SortBy?.ToLowerInvariant(), queryParams.SortOrder?.ToLowerInvariant()) switch
            {
                ("price", "desc") => query.OrderByDescending(product => product.Price).ThenBy(product => product.Id),
                ("price", _) => query.OrderBy(product => product.Price).ThenBy(product => product.Id),
                ("name", "desc") => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
                ("name", _) => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
                ("createdat", "asc") => query.OrderBy(product => product.CreatedAt).ThenBy(product => product.Id),
                _ => query.OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id)
            };

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<Product>(items, totalCount);
        }

        public Task<Product?> GetActiveByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
            => _context.Products
                .AsNoTracking()
                .Include(candidate => candidate.Category)
                .Include(candidate => candidate.Images)
                .AsSplitQuery()
                .FirstOrDefaultAsync(candidate => !candidate.IsDeleted
                    && candidate.Category != null
                    && !candidate.Category.IsDeleted
                    && candidate.Id == productId,
                    cancellationToken);

        public Task<bool> ActiveProductExistsAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
            => _context.Products
                .AsNoTracking()
                .AnyAsync(
                    product => !product.IsDeleted && product.Id == productId,
                    cancellationToken);

        public Task<Product?> GetActiveForCartAsync(
            Guid productId,
            CancellationToken cancellationToken = default)
            => _context.Products.FirstOrDefaultAsync(
                product => !product.IsDeleted && product.Id == productId,
                cancellationToken);

        public Task AddAsync(
            Product product,
            CancellationToken cancellationToken = default)
            => _context.Products.AddAsync(product, cancellationToken).AsTask();

        public Task LoadImagesAsync(
            Product product,
            CancellationToken cancellationToken = default)
            => _context.Entry(product)
                .Collection(candidate => candidate.Images)
                .LoadAsync(cancellationToken);

        public Task<ProductImage?> GetImageAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default)
            => _context.ProductImages.FirstOrDefaultAsync(
                candidate => candidate.Id == imageId
                    && candidate.ProductId == productId,
                cancellationToken);

        public Task<ProductImage?> GetReplacementImageAsync(
            Guid productId,
            Guid excludedImageId,
            CancellationToken cancellationToken = default)
            => _context.ProductImages
                .Where(candidate => candidate.ProductId == productId
                    && candidate.Id != excludedImageId)
                .OrderBy(candidate => candidate.Id)
                .FirstOrDefaultAsync(cancellationToken);

        public Task AddImageAsync(
            ProductImage image,
            CancellationToken cancellationToken = default)
            => _context.ProductImages.AddAsync(image, cancellationToken).AsTask();

        public void RemoveImage(ProductImage image)
            => _context.ProductImages.Remove(image);

        public async Task<IReadOnlyList<string>> GetImageUrlsAsync(
            CancellationToken cancellationToken = default)
            => await _context.ProductImages
                .AsNoTracking()
                .Select(image => image.ImageUrl)
                .ToListAsync(cancellationToken);
    }
}
