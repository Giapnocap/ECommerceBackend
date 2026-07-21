using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface ICategoryService
    {
        Task<IEnumerable<CategoryResponse>> GetAllAsync();
        Task<CategoryResponse> GetByIdAsync(Guid id);
        Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, Guid? actorUserId = null);
        Task<CategoryResponse> UpdateAsync(Guid id, UpdateCategoryRequest request, Guid? actorUserId = null);
        Task DeleteAsync(Guid id, Guid? actorUserId = null);
    }
}
