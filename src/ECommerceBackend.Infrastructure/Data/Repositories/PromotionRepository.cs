using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class PromotionRepository : IPromotionRepository
    {
        private readonly AppDbContext _context;

        public PromotionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PageSlice<Promotion>> GetPageAsync(
            bool? isActive,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Promotions
                .AsNoTracking()
                .AsQueryable();
            if (isActive.HasValue)
                query = query.Where(promotion =>
                    promotion.IsActive == isActive.Value);

            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(promotion => promotion.CreatedAt)
                .ThenBy(promotion => promotion.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<Promotion>(items, totalCount);
        }

        public Task<Promotion?> GetByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default)
            => _context.Promotions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    promotion => promotion.Id == promotionId,
                    cancellationToken);

        public Task<Promotion?> GetByNormalizedCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default)
            => _context.Promotions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    promotion =>
                        promotion.NormalizedCode == normalizedCode,
                    cancellationToken);

        public Task<Promotion?> LockByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return _context.Promotions
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Promotions] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {promotionId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return _context.Promotions.SingleOrDefaultAsync(
                promotion => promotion.Id == promotionId,
                cancellationToken);
        }

        public Task<Promotion?> LockByNormalizedCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return _context.Promotions
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Promotions] WITH (UPDLOCK, ROWLOCK) WHERE [NormalizedCode] = {normalizedCode}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return _context.Promotions.SingleOrDefaultAsync(
                promotion =>
                    promotion.NormalizedCode == normalizedCode,
                cancellationToken);
        }

        public Task<int> CountCustomerRedemptionsAsync(
            Guid promotionId,
            Guid userId,
            CancellationToken cancellationToken = default)
            => _context.PromotionRedemptions.CountAsync(
                redemption => redemption.PromotionId == promotionId
                    && redemption.UserId == userId,
                cancellationToken);

        public async Task<PromotionAnalyticsResponse?> GetAnalyticsByIdAsync(
            Guid promotionId,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken = default)
            => await BuildAnalyticsQuery(from, to)
                .SingleOrDefaultAsync(
                    analytics => analytics.PromotionId == promotionId,
                    cancellationToken);

        public async Task<PageSlice<PromotionAnalyticsResponse>> GetAnalyticsPageAsync(
            DateTime? from,
            DateTime? to,
            string sortBy,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var analytics = BuildAnalyticsQuery(from, to);
            var totalCount = await analytics.CountAsync(cancellationToken);
            var items = await SortAnalytics(analytics, sortBy)
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<PromotionAnalyticsResponse>(items, totalCount);
        }

        public Task AddAsync(
            Promotion promotion,
            CancellationToken cancellationToken = default)
            => _context.Promotions.AddAsync(
                promotion,
                cancellationToken).AsTask();

        public Task AddRedemptionAsync(
            PromotionRedemption redemption,
            CancellationToken cancellationToken = default)
            => _context.PromotionRedemptions.AddAsync(
                redemption,
                cancellationToken).AsTask();

        private IQueryable<PromotionAnalyticsResponse> BuildAnalyticsQuery(
            DateTime? from,
            DateTime? to)
        {
            var rangeFrom = from;
            var rangeTo = to;
            var redemptions =
                from redemption in _context.PromotionRedemptions.AsNoTracking()
                join order in _context.Orders.AsNoTracking()
                    on redemption.OrderId equals order.Id
                where (!rangeFrom.HasValue || redemption.CreatedAt >= rangeFrom.Value)
                    && (!rangeTo.HasValue || redemption.CreatedAt < rangeTo.Value)
                select new
                {
                    redemption.PromotionId,
                    redemption.OrderId,
                    redemption.DiscountAmount,
                    order.BaseSubtotalAmount
                };

            return
                from promotion in _context.Promotions.AsNoTracking()
                join redemption in redemptions
                    on promotion.Id equals redemption.PromotionId into promotionRedemptions
                select new PromotionAnalyticsResponse
                {
                    PromotionId = promotion.Id,
                    Code = promotion.Code,
                    IsActive = promotion.IsActive,
                    StartsAt = promotion.StartsAt,
                    EndsAt = promotion.EndsAt,
                    UsageCount = promotionRedemptions.Count(),
                    GeneratedOrderCount = promotionRedemptions
                        .Select(redemption => redemption.OrderId)
                        .Distinct()
                        .Count(),
                    GrossRevenue = promotionRedemptions
                        .Sum(redemption =>
                            (decimal?)redemption.BaseSubtotalAmount) ?? 0,
                    DiscountAmount = promotionRedemptions
                        .Sum(redemption => (decimal?)redemption.DiscountAmount) ?? 0,
                    NetRevenue = (promotionRedemptions
                        .Sum(redemption =>
                            (decimal?)redemption.BaseSubtotalAmount) ?? 0)
                        - (promotionRedemptions
                            .Sum(redemption => (decimal?)redemption.DiscountAmount) ?? 0)
                };
        }

        private static IOrderedQueryable<PromotionAnalyticsResponse> SortAnalytics(
            IQueryable<PromotionAnalyticsResponse> analytics,
            string sortBy)
            => sortBy switch
            {
                "grossRevenue" => analytics
                    .OrderByDescending(item => item.GrossRevenue)
                    .ThenBy(item => item.Code)
                    .ThenBy(item => item.PromotionId),
                "discountAmount" => analytics
                    .OrderByDescending(item => item.DiscountAmount)
                    .ThenBy(item => item.Code)
                    .ThenBy(item => item.PromotionId),
                "netRevenue" => analytics
                    .OrderByDescending(item => item.NetRevenue)
                    .ThenBy(item => item.Code)
                    .ThenBy(item => item.PromotionId),
                _ => analytics
                    .OrderByDescending(item => item.UsageCount)
                    .ThenBy(item => item.Code)
                    .ThenBy(item => item.PromotionId)
            };
    }
}
