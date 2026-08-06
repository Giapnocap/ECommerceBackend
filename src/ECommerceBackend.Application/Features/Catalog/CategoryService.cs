using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;

        public CategoryService(
            ICategoryRepository categoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IAuditWriter? auditWriter = null)
        {
            _categoryRepository = categoryRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        public async Task<IEnumerable<CategoryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            var categories =
                await _categoryRepository.GetRootCategoriesAsync(
                    cancellationToken);

            return categories.Select(category => category.ToResponse());
        }

        public async Task<CategoryResponse> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var category = await _categoryRepository.GetActiveByIdAsync(
                id,
                cancellationToken)
                ?? throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");

            return category.ToResponse();
        }

        public async Task<CategoryResponse> CreateAsync(
            CreateCategoryRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            Guid categoryId;
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var name = request.Name.Trim();
            var normalizedName = DomainRuleGuard.AsBusiness(() =>
                Category.NormalizeName(name));

            try
            {
                var parent = await LoadParentForUpdateAsync(
                    request.ParentId,
                    cancellationToken);

                if (await _categoryRepository.ExistsAtLevelAsync(
                    normalizedName,
                    request.ParentId,
                    excludedCategoryId: null,
                    cancellationToken))
                {
                    throw new ConflictException($"Danh mục '{name}' đã tồn tại trong cùng cấp.");
                }

                var category = DomainRuleGuard.AsBusiness(() =>
                    Category.Create(
                        Guid.NewGuid(),
                        name,
                        parent));
                categoryId = category.Id;

                await _categoryRepository.AddAsync(category, cancellationToken);
                _audit.Write(
                    "category.create",
                    "Category",
                    category.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?> { ["parentId"] = category.ParentId });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsUniqueConstraintViolation(ex))
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

            return await GetByIdAsync(categoryId, cancellationToken);
        }

        public async Task<CategoryResponse> UpdateAsync(
            Guid id,
            UpdateCategoryRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var name = request.Name.Trim();
            var normalizedName = DomainRuleGuard.AsBusiness(() =>
                Category.NormalizeName(name));

            try
            {
                var lockedCategories = await LoadCategoriesForUpdateAsync(
                    request.ParentId.HasValue ? [id, request.ParentId.Value] : [id],
                    cancellationToken);
                if (!lockedCategories.TryGetValue(id, out var category))
                    throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");
                if (request.ParentId.HasValue
                    && !lockedCategories.ContainsKey(request.ParentId.Value))
                {
                    throw new NotFoundException("Danh mục cha không tồn tại.");
                }
                var parent = request.ParentId.HasValue
                    ? lockedCategories[request.ParentId.Value]
                    : null;

                await _categoryRepository.LoadChildrenAsync(
                    category,
                    cancellationToken);
                DomainRuleGuard.AsBusiness(() =>
                    category.EnsureCanUpdateDetails(name, parent));

                if (await _categoryRepository.ExistsAtLevelAsync(
                    normalizedName,
                    request.ParentId,
                    id,
                    cancellationToken))
                {
                    throw new ConflictException($"Danh mục '{name}' đã tồn tại trong cùng cấp.");
                }

                DomainRuleGuard.AsBusiness(() =>
                    category.UpdateDetails(name, parent));
                _audit.Write(
                    "category.update",
                    "Category",
                    category.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?> { ["parentId"] = category.ParentId });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException($"Danh mục '{name}' vừa được cập nhật bởi một yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

            return await GetByIdAsync(id, cancellationToken);
        }

        public async Task DeleteAsync(
            Guid id,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var category = await LoadCategoryForUpdateAsync(id, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy danh mục với Id '{id}'.");
                await _categoryRepository.LoadChildrenAndProductsAsync(
                    category,
                    cancellationToken);

                DomainRuleGuard.AsBusiness(category.MarkDeleted);
                _audit.Write("category.delete", "Category", category.Id.ToString(), actorUserId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

        private async Task<Category?> LoadParentForUpdateAsync(
            Guid? parentId,
            CancellationToken cancellationToken = default)
        {
            if (!parentId.HasValue)
                return null;

            var parent = await LoadCategoryForUpdateAsync(
                parentId.Value,
                cancellationToken)
                ?? throw new NotFoundException("Danh mục cha không tồn tại.");

            return parent;
        }

        private static ConflictException CategoryConcurrencyConflict(Exception exception)
            => new(
                "category_concurrency_conflict",
                "Danh mục đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                exception);

        private async Task<Dictionary<Guid, Category>> LoadCategoriesForUpdateAsync(
            IEnumerable<Guid> categoryIds,
            CancellationToken cancellationToken)
        {
            var categories = new Dictionary<Guid, Category>();
            foreach (var categoryId in categoryIds.Distinct().OrderBy(candidate => candidate))
            {
                var category = await LoadCategoryForUpdateAsync(
                    categoryId,
                    cancellationToken);
                if (category != null)
                    categories.Add(categoryId, category);
            }

            return categories;
        }

        private async Task<Category?> LoadCategoryForUpdateAsync(
            Guid categoryId,
            CancellationToken cancellationToken)
            => await _consistency.LockCategoryAsync(
                categoryId,
                activeOnly: true,
                cancellationToken);

    }
}
