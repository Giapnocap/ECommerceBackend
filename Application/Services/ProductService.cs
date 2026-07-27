using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public class ProductService : IProductService
    {
        private static readonly Meter CatalogMeter = new("ECommerceBackend.Catalog");
        private static readonly Counter<long> CatalogQueryCounter =
            CatalogMeter.CreateCounter<long>("catalog.queries");
        private static readonly Histogram<double> CatalogQueryDuration =
            CatalogMeter.CreateHistogram<double>("catalog.query.duration", "ms");
        private static readonly Histogram<long> CatalogQueryResultCount =
            CatalogMeter.CreateHistogram<long>("catalog.query.result_count", "item");

        private readonly IProductRepository _productRepository;
        private readonly IInventoryRepository _inventoryRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;

        public ProductService(
            IProductRepository productRepository,
            IInventoryRepository inventoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency)
            : this(
                productRepository,
                inventoryRepository,
                unitOfWork,
                consistency,
                TimeProvider.System)
        {
        }

        public ProductService(
            IProductRepository productRepository,
            IInventoryRepository inventoryRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _productRepository = productRepository;
            _inventoryRepository = inventoryRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<PagedResult<ProductResponse>> GetAllAsync(
            ProductQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            PageSlice<Product> result;
            try
            {
                result = await _productRepository.GetPageAsync(
                    queryParams,
                    Paging.GetSkipCount(paging),
                    paging.Size,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                RecordCatalogQuery(queryParams, stopwatch, "cancelled", resultCount: null);
                throw;
            }
            catch
            {
                RecordCatalogQuery(queryParams, stopwatch, "failed", resultCount: null);
                throw;
            }

            RecordCatalogQuery(
                queryParams,
                stopwatch,
                "success",
                result.Items.Count);

            return PagedResult<ProductResponse>.Create(
                result.Items.Select(product => product.ToResponse()),
                result.TotalCount,
                paging.Page,
                paging.Size);
        }

        private static void RecordCatalogQuery(
            ProductQueryParams queryParams,
            Stopwatch stopwatch,
            string outcome,
            int? resultCount)
        {
            stopwatch.Stop();
            var tags = new TagList
            {
                { "catalog.has_search", !string.IsNullOrWhiteSpace(queryParams.Keyword) },
                { "catalog.has_category", queryParams.CategoryId.HasValue },
                { "catalog.sort", NormalizeSortTag(queryParams.SortBy, queryParams.SortOrder) },
                { "catalog.outcome", outcome }
            };
            CatalogQueryCounter.Add(1, tags);
            CatalogQueryDuration.Record(stopwatch.Elapsed.TotalMilliseconds, tags);
            if (resultCount.HasValue)
                CatalogQueryResultCount.Record(resultCount.Value, tags);
        }

        public async Task<ProductResponse> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var product = await _productRepository.GetActiveByIdAsync(
                id,
                cancellationToken)
                ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");

            return product.ToResponse();
        }

        public async Task<ProductResponse> CreateAsync(
            CreateProductRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            Guid productId;
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                _ = await LoadCategoryForUpdateAsync(
                    request.CategoryId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy danh mục.");

                var occurredAt = UtcNow;
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    Name = request.Name.Trim(),
                    Price = request.Price,
                    StockQuantity = 0,
                    Description = request.Description.Trim(),
                    CategoryId = request.CategoryId,
                    CreatedAt = occurredAt
                };
                var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.AdjustTo(product, request.StockQuantity));
                productId = product.Id;

                await _productRepository.AddAsync(product, cancellationToken);
                if (inventoryMutation.QuantityChange != 0)
                {
                    _inventoryRepository.AddTransaction(new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        CreatedByUserId = actorUserId,
                        Type = Domain.Enums.InventoryTransactionType.InitialStock,
                        QuantityChange = inventoryMutation.QuantityChange,
                        BalanceAfter = inventoryMutation.BalanceAfter,
                        Reason = "Tồn kho ban đầu",
                        CreatedAt = occurredAt
                    });
                }
                _audit.Write(
                    "product.create",
                    "Product",
                    product.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["categoryId"] = product.CategoryId,
                        ["initialStock"] = product.StockQuantity
                    });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CatalogueConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }

            return await GetByIdAsync(productId, cancellationToken);
        }

        public async Task<ProductResponse> UpdateAsync(
            Guid id,
            UpdateProductRequest request,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                _ = await LoadCategoryForUpdateAsync(
                    request.CategoryId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy danh mục.");
                var product = await LoadProductForUpdateAsync(id, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");
                var occurredAt = UtcNow;
                var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.AdjustTo(product, request.StockQuantity));

                product.Name = request.Name.Trim();
                product.Price = request.Price;
                product.Description = request.Description.Trim();
                product.CategoryId = request.CategoryId;

                if (inventoryMutation.QuantityChange != 0)
                {
                    _inventoryRepository.AddTransaction(new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = product.Id,
                        CreatedByUserId = actorUserId,
                        Type = Domain.Enums.InventoryTransactionType.ManualAdjustment,
                        QuantityChange = inventoryMutation.QuantityChange,
                        BalanceAfter = inventoryMutation.BalanceAfter,
                        Reason = "Cập nhật tồn kho sản phẩm",
                        CreatedAt = occurredAt
                    });
                }

                _audit.Write(
                    "product.update",
                    "Product",
                    product.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["categoryId"] = product.CategoryId,
                        ["stockAdjustment"] = inventoryMutation.QuantityChange,
                        ["stockBalance"] = inventoryMutation.BalanceAfter
                    });

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Sản phẩm vừa được cập nhật bởi yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CatalogueConcurrencyConflict(ex);
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
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var product = await LoadProductForUpdateAsync(id, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");

                product.IsDeleted = true;
                _audit.Write("product.delete", "Product", product.Id.ToString(), actorUserId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Sản phẩm vừa được cập nhật bởi yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw CatalogueConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }
        private static ConflictException CatalogueConcurrencyConflict(Exception exception)
            => new(
                "catalogue_concurrency_conflict",
                "Dữ liệu sản phẩm đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                exception);

        private static string NormalizeSortTag(string? sortBy, string? sortOrder)
        {
            var field = sortBy?.ToLowerInvariant() ?? "createdat";
            var order = sortOrder?.ToLowerInvariant()
                ?? (field == "createdat" ? "desc" : "asc");
            return $"{field}:{order}";
        }

        private async Task<Category?> LoadCategoryForUpdateAsync(
            Guid categoryId,
            CancellationToken cancellationToken)
            => await _consistency.LockCategoryAsync(
                categoryId,
                activeOnly: true,
                cancellationToken);

        private async Task<Product?> LoadProductForUpdateAsync(
            Guid productId,
            CancellationToken cancellationToken)
            => await _consistency.LockProductAsync(
                productId,
                activeOnly: true,
                cancellationToken);
    }
}
