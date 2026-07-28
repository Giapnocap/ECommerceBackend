using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class CategoryRepository : ICategoryRepository
    {
        private readonly AppDbContext _context;

        public CategoryRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Category>> GetRootCategoriesAsync(
            CancellationToken cancellationToken = default)
            => await _context.Categories
                .AsNoTracking()
                .Include(category => category.Children.Where(child => !child.IsDeleted))
                .Include(category => category.Parent)
                .Where(category => !category.IsDeleted && category.ParentId == null)
                .OrderBy(category => category.Name)
                .ThenBy(category => category.Id)
                .ToListAsync(cancellationToken);

        public Task<Category?> GetActiveByIdAsync(
            Guid categoryId,
            CancellationToken cancellationToken = default)
            => _context.Categories
                .AsNoTracking()
                .Include(candidate => candidate.Children.Where(child => !child.IsDeleted))
                .Include(candidate => candidate.Parent)
                .FirstOrDefaultAsync(
                    candidate => !candidate.IsDeleted && candidate.Id == categoryId,
                    cancellationToken);

        public Task<bool> ExistsAtLevelAsync(
            string normalizedName,
            Guid? parentId,
            Guid? excludedCategoryId,
            CancellationToken cancellationToken = default)
            => _context.Categories.AnyAsync(
                category => !category.IsDeleted
                    && category.NormalizedName == normalizedName
                    && category.ParentId == parentId
                    && (!excludedCategoryId.HasValue
                        || category.Id != excludedCategoryId.Value),
                cancellationToken);

        public Task AddAsync(
            Category category,
            CancellationToken cancellationToken = default)
            => _context.Categories.AddAsync(category, cancellationToken).AsTask();

        public Task LoadChildrenAsync(
            Category category,
            CancellationToken cancellationToken = default)
            => _context.Entry(category)
                .Collection(candidate => candidate.Children)
                .LoadAsync(cancellationToken);

        public async Task LoadChildrenAndProductsAsync(
            Category category,
            CancellationToken cancellationToken = default)
        {
            await LoadChildrenAsync(category, cancellationToken);
            await _context.Entry(category)
                .Collection(candidate => candidate.Products)
                .LoadAsync(cancellationToken);
        }
    }
}
