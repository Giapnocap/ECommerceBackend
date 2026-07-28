using ECommerceBackend.Application.Common;
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
    }
}
