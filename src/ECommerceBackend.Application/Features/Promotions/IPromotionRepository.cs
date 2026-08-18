using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IPromotionRepository
    {
        Task<PageSlice<Promotion>> GetPageAsync(
            bool? isActive,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<Promotion?> GetByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default);

        Task<Promotion?> GetByNormalizedCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default);

        Task<Promotion?> LockByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default);

        Task<Promotion?> LockByNormalizedCodeAsync(
            string normalizedCode,
            CancellationToken cancellationToken = default);

        Task<int> CountCustomerRedemptionsAsync(
            Guid promotionId,
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<PromotionAnalyticsResponse?> GetAnalyticsByIdAsync(
            Guid promotionId,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken = default);

        Task<PageSlice<PromotionAnalyticsResponse>> GetAnalyticsPageAsync(
            DateTime? from,
            DateTime? to,
            string sortBy,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Promotion promotion,
            CancellationToken cancellationToken = default);

        Task AddRedemptionAsync(
            PromotionRedemption redemption,
            CancellationToken cancellationToken = default);
    }
}
