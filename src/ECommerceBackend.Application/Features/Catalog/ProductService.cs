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

        public async Task<PagedResult<ProductSummaryResponse>> GetSummariesAsync(
            ProductQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize);
            PageSlice<ProductSummaryResponse> result;
            try
            {
                result = await _productRepository.GetSummaryPageAsync(
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

            return PagedResult<ProductSummaryResponse>.Create(
                result.Items,
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
                var product = DomainRuleGuard.AsBusiness(() =>
                    Product.Create(
                        Guid.NewGuid(),
                        request.CategoryId,
                        request.Name,
                        request.Price,
                        request.StockQuantity,
                        request.Description,
                        occurredAt));
                productId = product.Id;

                await _productRepository.AddAsync(product, cancellationToken);
                if (product.StockQuantity != 0)
                {
                    _inventoryRepository.AddTransaction(
                        DomainRuleGuard.AsBusiness(() =>
                            InventoryTransaction.Create(
                                Guid.NewGuid(),
                                product.Id,
                                (Guid?)null,
                                actorUserId,
                                Domain.Enums.InventoryTransactionType.InitialStock,
                                new InventoryMutation(
                                    product.StockQuantity,
                                    product.StockQuantity),
                                "Tồn kho ban đầu",
                                occurredAt)));
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
                if (request.StockQuantity != product.StockQuantity)
                {
                    throw new ConflictException(
                        "inventory_adjustment_required",
                        "Tồn kho đã thay đổi hoặc phải được điều chỉnh qua endpoint tồn kho riêng.");
                }

                DomainRuleGuard.AsBusiness(() =>
                    product.UpdateDetails(
                        request.CategoryId,
                        request.Name,
                        request.Price,
                        request.Description));

                _audit.Write(
                    "product.update",
                    "Product",
                    product.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["categoryId"] = product.CategoryId
                    });

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

        public async Task<ProductResponse> AdjustStockAsync(
            Guid id,
            AdjustProductStockRequest request,
            byte[] expectedRowVersion,
            Guid? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            if (expectedRowVersion is not { Length: > 0 })
            {
                throw new BusinessException(
                    "inventory_version_invalid",
                    "Phiên bản tồn kho không hợp lệ.");
            }

            var reason = NormalizeInventoryAdjustmentReason(request.Reason);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var product = await LoadProductForUpdateAsync(id, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");
                if (!product.RowVersion.AsSpan().SequenceEqual(expectedRowVersion))
                {
                    throw new ConflictException(
                        "inventory_version_conflict",
                        "Tồn kho đã được thay đổi bởi yêu cầu khác. Vui lòng tải lại sản phẩm.");
                }

                var occurredAt = UtcNow;
                var inventoryMutation = DomainRuleGuard.AsBusiness(() =>
                    product.AdjustStockTo(request.TargetQuantity));
                if (inventoryMutation.QuantityChange == 0)
                {
                    throw new BusinessException(
                        "inventory_adjustment_empty",
                        "Tồn kho mục tiêu phải khác tồn kho hiện tại.");
                }

                _inventoryRepository.AddTransaction(
                    DomainRuleGuard.AsBusiness(() =>
                        InventoryTransaction.Create(
                            Guid.NewGuid(),
                            product.Id,
                            (Guid?)null,
                            actorUserId,
                            Domain.Enums.InventoryTransactionType.ManualAdjustment,
                            inventoryMutation,
                            reason,
                            occurredAt)));
                _audit.Write(
                    "inventory.adjust",
                    "Product",
                    product.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["quantityChange"] = inventoryMutation.QuantityChange,
                        ["stockBalance"] = inventoryMutation.BalanceAfter
                    });

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "inventory_version_conflict",
                    "Tồn kho đã được thay đổi bởi yêu cầu khác. Vui lòng tải lại sản phẩm.",
                    ex);
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

                DomainRuleGuard.AsBusiness(product.MarkDeleted);
                _audit.Write("product.delete", "Product", product.Id.ToString(), actorUserId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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

        private static string NormalizeInventoryAdjustmentReason(string reason)
        {
            var normalizedReason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedReason)
                || normalizedReason.Length > 500)
            {
                throw new BusinessException(
                    "inventory_adjustment_reason_invalid",
                    "Lý do điều chỉnh tồn kho phải có từ 1 đến 500 ký tự.");
            }

            return normalizedReason;
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
