using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface ICategoryRepository
    {
        Task<IReadOnlyList<Category>> GetRootCategoriesAsync(
            CancellationToken cancellationToken = default);

        Task<Category?> GetActiveByIdAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default);

        Task<bool> ExistsAtLevelAsync(
            string normalizedName,
            Guid? parentId,
            Guid? excludedCategoryId,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Category category,
            CancellationToken cancellationToken = default);

        Task LoadChildrenAsync(
            Category category,
            CancellationToken cancellationToken = default);

        Task LoadChildrenAndProductsAsync(
            Category category,
            CancellationToken cancellationToken = default);
    }
}
