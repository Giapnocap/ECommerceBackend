using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using AutoMapper;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
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

        private readonly IGenericRepository<Product> _productRepo;
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IMapper _mapper;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;

        public ProductService(
            IGenericRepository<Product> productRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IMapper mapper)
            : this(productRepo, context, consistency, mapper, TimeProvider.System)
        {
        }

        public ProductService(
            IGenericRepository<Product> productRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IMapper mapper,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _productRepo = productRepo;
            _context = context;
            _consistency = consistency;
            _mapper = mapper;
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
            var query = _productRepo.Query()
                .AsNoTracking()
                .Where(product => !product.IsDeleted
                    && product.Category != null
                    && !product.Category.IsDeleted)
                .Include(product => product.Category)
                .Include(product => product.Images)
                .AsSplitQuery()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                query = query.Where(product => product.Name.Contains(keyword)
                    || product.Description.Contains(keyword));
            }

            if (queryParams.CategoryId.HasValue)
                query = query.Where(product => product.CategoryId == queryParams.CategoryId.Value);

            if (queryParams.MinPrice.HasValue)
                query = query.Where(product => product.Price >= queryParams.MinPrice.Value);

            if (queryParams.MaxPrice.HasValue)
                query = query.Where(product => product.Price <= queryParams.MaxPrice.Value);

            query = (queryParams.SortBy?.ToLowerInvariant(), queryParams.SortOrder?.ToLowerInvariant()) switch
            {
                ("price", "desc") => query.OrderByDescending(product => product.Price).ThenBy(product => product.Id),
                ("price", _) => query.OrderBy(product => product.Price).ThenBy(product => product.Id),
                ("name", "desc") => query.OrderByDescending(product => product.Name).ThenBy(product => product.Id),
                ("name", _) => query.OrderBy(product => product.Name).ThenBy(product => product.Id),
                ("createdat", "asc") => query.OrderBy(product => product.CreatedAt).ThenBy(product => product.Id),
                _ => query.OrderByDescending(product => product.CreatedAt).ThenByDescending(product => product.Id)
            };

            int totalCount;
            List<Product> items;
            try
            {
                totalCount = await query.CountAsync(cancellationToken);
                items = await query
                    .Skip(Paging.GetSkipCount(paging))
                    .Take(paging.Size)
                    .ToListAsync(cancellationToken);
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

            RecordCatalogQuery(queryParams, stopwatch, "success", items.Count);

            return PagedResult<ProductResponse>.Create(
                _mapper.Map<IEnumerable<ProductResponse>>(items),
                totalCount,
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
            var product = await _productRepo.Query()
                .AsNoTracking()
                .Include(candidate => candidate.Category)
                .Include(candidate => candidate.Images)
                .AsSplitQuery()
                .FirstOrDefaultAsync(candidate => !candidate.IsDeleted
                    && candidate.Category != null
                    && !candidate.Category.IsDeleted
                    && candidate.Id == id,
                    cancellationToken)
                ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");

            return _mapper.Map<ProductResponse>(product);
        }

        public async Task<ProductResponse> CreateAsync(CreateProductRequest request, Guid? actorUserId = null)
        {
            Guid productId;
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                _ = await LoadCategoryForUpdateAsync(request.CategoryId)
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

                await _productRepo.AddAsync(product);
                if (inventoryMutation.QuantityChange != 0)
                {
                    _context.InventoryTransactions.Add(new InventoryTransaction
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
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
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

            return await GetByIdAsync(productId);
        }

        public async Task<ProductResponse> UpdateAsync(
            Guid id,
            UpdateProductRequest request,
            Guid? actorUserId = null)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                _ = await LoadCategoryForUpdateAsync(request.CategoryId)
                    ?? throw new NotFoundException("Không tìm thấy danh mục.");
                var product = await LoadProductForUpdateAsync(id)
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
                    _context.InventoryTransactions.Add(new InventoryTransaction
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

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
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

            return await GetByIdAsync(id);
        }

        public async Task DeleteAsync(Guid id, Guid? actorUserId = null)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);

            try
            {
                var product = await LoadProductForUpdateAsync(id)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{id}'.");

                product.IsDeleted = true;
                _audit.Write("product.delete", "Product", product.Id.ToString(), actorUserId);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
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

        private async Task<Category?> LoadCategoryForUpdateAsync(Guid categoryId)
            => await _consistency.LockCategoryAsync(categoryId, activeOnly: true);

        private async Task<Product?> LoadProductForUpdateAsync(Guid productId)
            => await _consistency.LockProductAsync(productId, activeOnly: true);
    }
}
