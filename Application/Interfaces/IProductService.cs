using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IProductService
    {
        Task<PagedResult<ProductResponse>> GetAllAsync(
            ProductQueryParams queryParams,
            CancellationToken cancellationToken = default);
        Task<ProductResponse> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<ProductResponse> CreateAsync(CreateProductRequest request, Guid? actorUserId = null);
        Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, Guid? actorUserId = null);
        Task DeleteAsync(Guid id, Guid? actorUserId = null);
    }
}
