using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IProductService
    {
        Task<PagedResult<ProductResponse>> GetAllAsync(
            ProductQueryParams queryParams,
            CancellationToken cancellationToken = default);
        Task<PagedResult<ProductSummaryResponse>> GetSummariesAsync(
            ProductQueryParams queryParams,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<ProductResponse> CreateAsync(
            CreateProductRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> UpdateAsync(
            Guid id,
            UpdateProductRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> AdjustStockAsync(
            Guid id,
            AdjustProductStockRequest request,
            byte[] expectedRowVersion,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> StockInAsync(
            Guid id,
            StockInRequest request,
            byte[] expectedRowVersion,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> UpdateLowStockThresholdAsync(
            Guid id,
            UpdateLowStockThresholdRequest request,
            byte[] expectedRowVersion,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task DeleteAsync(
            Guid id,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
    }
}
