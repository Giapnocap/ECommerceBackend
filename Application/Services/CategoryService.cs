using System.Data;
using AutoMapper;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly IGenericRepository<Category> _categoryRepo;
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IMapper _mapper;
        private readonly IAuditWriter _audit;

        public CategoryService(
            IGenericRepository<Category> categoryRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IMapper mapper,
            IAuditWriter? auditWriter = null)
        {
            _categoryRepo = categoryRepo;
            _context = context;
            _consistency = consistency;
            _mapper = mapper;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        public async Task<IEnumerable<CategoryResponse>> GetAllAsync()
        {
            var categories = await _categoryRepo.Query()
                .AsNoTracking()
                .Include(category => category.Children.Where(child => !child.IsDeleted))
                .Include(category => category.Parent)
                .Where(category => !category.IsDeleted && category.ParentId == null)
                .OrderBy(category => category.Name)
                .ThenBy(category => category.Id)
                .ToListAsync();

            return _mapper.Map<IEnumerable<CategoryResponse>>(categories);
        }

        public async Task<CategoryResponse> GetByIdAsync(Guid id)
        {
            var category = await _categoryRepo.Query()
                .AsNoTracking()
                .Include(candidate => candidate.Children.Where(child => !child.IsDeleted))
                .Include(candidate => candidate.Parent)
                .FirstOrDefaultAsync(candidate => !candidate.IsDeleted && candidate.Id == id)
                ?? throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");

            return _mapper.Map<CategoryResponse>(category);
        }

        public async Task<CategoryResponse> CreateAsync(CreateCategoryRequest request, Guid? actorUserId = null)
        {
            Guid categoryId;
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.Serializable);
            var name = request.Name.Trim();
            var normalizedName = Normalize(name);

            try
            {
                await ValidateParentAsync(request.ParentId);

                if (await _context.Categories.AnyAsync(category => !category.IsDeleted
                    && category.NormalizedName == normalizedName
                    && category.ParentId == request.ParentId))
                {
                    throw new ConflictException($"Danh mục '{name}' đã tồn tại trong cùng cấp.");
                }

                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    NormalizedName = normalizedName,
                    ParentId = request.ParentId
                };
                categoryId = category.Id;

                await _categoryRepo.AddAsync(category);
                _audit.Write(
                    "category.create",
                    "Category",
                    category.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?> { ["parentId"] = category.ParentId });
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException($"Danh mục '{name}' đã được tạo bởi một yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CategoryConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(categoryId);
        }

        public async Task<CategoryResponse> UpdateAsync(
            Guid id,
            UpdateCategoryRequest request,
            Guid? actorUserId = null)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.Serializable);
            var name = request.Name.Trim();
            var normalizedName = Normalize(name);

            try
            {
                var lockedCategories = await LoadCategoriesForUpdateAsync(
                    request.ParentId.HasValue ? [id, request.ParentId.Value] : [id]);
                if (!lockedCategories.TryGetValue(id, out var category))
                    throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");
                if (request.ParentId.HasValue
                    && !lockedCategories.ContainsKey(request.ParentId.Value))
                {
                    throw new NotFoundException("Danh mục cha không tồn tại.");
                }

                await _context.Entry(category)
                    .Collection(candidate => candidate.Children)
                    .LoadAsync();

                await ValidateParentAsync(request.ParentId, id);

                if (category.Children.Any(child => !child.IsDeleted) && request.ParentId.HasValue)
                    throw new BusinessException("Không thể chuyển danh mục có con thành danh mục con.");

                if (await _context.Categories.AnyAsync(candidate => !candidate.IsDeleted
                    && candidate.NormalizedName == normalizedName
                    && candidate.ParentId == request.ParentId
                    && candidate.Id != id))
                {
                    throw new ConflictException($"Danh mục '{name}' đã tồn tại trong cùng cấp.");
                }

                category.Name = name;
                category.NormalizedName = normalizedName;
                category.ParentId = request.ParentId;
                _audit.Write(
                    "category.update",
                    "Category",
                    category.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?> { ["parentId"] = category.ParentId });
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException($"Danh mục '{name}' vừa được cập nhật bởi một yêu cầu khác.", ex);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Danh mục vừa được cập nhật bởi một yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CategoryConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(id);
        }

        public async Task DeleteAsync(Guid id, Guid? actorUserId = null)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.Serializable);

            try
            {
                var category = await LoadCategoryForUpdateAsync(id)
                    ?? throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");
                await _context.Entry(category).Collection(candidate => candidate.Children).LoadAsync();
                await _context.Entry(category).Collection(candidate => candidate.Products).LoadAsync();

                if (category.Children.Any(child => !child.IsDeleted))
                    throw new BusinessException("Không thể xóa danh mục đang có danh mục con.");

                if (category.Products.Any(product => !product.IsDeleted))
                    throw new BusinessException("Không thể xóa danh mục đang có sản phẩm.");

                category.IsDeleted = true;
                _audit.Write("category.delete", "Category", category.Id.ToString(), actorUserId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Danh mục vừa được cập nhật bởi một yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CategoryConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task ValidateParentAsync(Guid? parentId, Guid? categoryId = null)
        {
            if (!parentId.HasValue)
                return;

            if (parentId == categoryId)
                throw new BusinessException("Danh mục không thể là cha của chính nó.");

            var parent = await LoadCategoryForUpdateAsync(parentId.Value)
                ?? throw new NotFoundException("Danh mục cha không tồn tại.");

            if (categoryId.HasValue && parent.ParentId == categoryId)
                throw new BusinessException("Không thể chọn danh mục con làm danh mục cha.");

            if (parent.ParentId.HasValue)
                throw new BusinessException("Chỉ hỗ trợ tối đa 2 cấp danh mục.");
        }

        private static ConflictException CategoryConcurrencyConflict(Exception exception)
            => new(
                "category_concurrency_conflict",
                "Danh mục đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                exception);

        private async Task<Dictionary<Guid, Category>> LoadCategoriesForUpdateAsync(
            IEnumerable<Guid> categoryIds)
        {
            var categories = new Dictionary<Guid, Category>();
            foreach (var categoryId in categoryIds.Distinct().OrderBy(candidate => candidate))
            {
                var category = await LoadCategoryForUpdateAsync(categoryId);
                if (category != null)
                    categories.Add(categoryId, category);
            }

            return categories;
        }

        private async Task<Category?> LoadCategoryForUpdateAsync(Guid categoryId)
            => await _consistency.LockCategoryAsync(categoryId, activeOnly: true);

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    }
}
