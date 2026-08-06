using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IProductRepository
    {
        Task<PageSlice<Product>> GetPageAsync(
            ProductQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<PageSlice<ProductSummaryResponse>> GetSummaryPageAsync(
            ProductQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<Product?> GetActiveByIdAsync(
            Guid productId,
            CancellationToken cancellationToken = default);

        Task<bool> ActiveProductExistsAsync(
            Guid productId,
            CancellationToken cancellationToken = default);

        Task<Product?> GetActiveForCartAsync(
            Guid productId,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Product product,
            CancellationToken cancellationToken = default);

        Task LoadImagesAsync(
            Product product,
            CancellationToken cancellationToken = default);

        Task<ProductImage?> GetImageAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default);

        Task<ProductImage?> GetReplacementImageAsync(
            Guid productId,
            Guid excludedImageId,
            CancellationToken cancellationToken = default);

        Task AddImageAsync(
            ProductImage image,
            CancellationToken cancellationToken = default);

        void RemoveImage(ProductImage image);

        Task<IReadOnlyList<string>> GetImageUrlsAsync(
            CancellationToken cancellationToken = default);
    }
}
