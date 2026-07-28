using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface ICategoryService
    {
        Task<IEnumerable<CategoryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default);
        Task<CategoryResponse> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);
        Task<CategoryResponse> CreateAsync(
            CreateCategoryRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task<CategoryResponse> UpdateAsync(
            Guid id,
            UpdateCategoryRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
        Task DeleteAsync(
            Guid id,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default);
    }
}
