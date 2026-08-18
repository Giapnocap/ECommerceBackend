using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IPromotionService
    {
        Task<PagedResult<PromotionResponse>> GetAllAsync(
            PromotionQueryParams query,
            CancellationToken cancellationToken = default);

        Task<PromotionResponse> GetByIdAsync(
            Guid promotionId,
            CancellationToken cancellationToken = default);

        Task<PromotionAnalyticsResponse> GetAnalyticsAsync(
            Guid promotionId,
            PromotionAnalyticsRangeQuery query,
            CancellationToken cancellationToken = default);

        Task<PagedResult<PromotionAnalyticsResponse>> GetAnalyticsAsync(
            PromotionAnalyticsQuery query,
            CancellationToken cancellationToken = default);

        Task<PromotionResponse> CreateAsync(
            CreatePromotionRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default);

        Task<PromotionResponse> UpdateAsync(
            Guid promotionId,
            UpdatePromotionRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default);

        Task DeactivateAsync(
            Guid promotionId,
            Guid actorUserId,
            CancellationToken cancellationToken = default);
    }
}
