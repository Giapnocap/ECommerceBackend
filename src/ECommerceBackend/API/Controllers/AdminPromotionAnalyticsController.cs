using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/admin/promotions")]
    [Route("api/v{version:apiVersion}/admin/promotions")]
    [Authorize(Policy = PermissionNames.ManageProducts)]
    [Produces("application/json")]
    public sealed class AdminPromotionAnalyticsController : ControllerBase
    {
        private readonly IPromotionService _promotionService;

        public AdminPromotionAnalyticsController(IPromotionService promotionService)
        {
            _promotionService = promotionService;
        }

        [HttpGet("analytics")]
        [ProducesResponseType(
            typeof(PagedResult<PromotionAnalyticsResponse>),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalytics(
            [FromQuery] PromotionAnalyticsQuery query,
            CancellationToken cancellationToken)
            => Ok(await _promotionService.GetAnalyticsAsync(query, cancellationToken));

        [HttpGet("{id:guid}/analytics")]
        [ProducesResponseType(typeof(PromotionAnalyticsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalyticsByPromotion(
            Guid id,
            [FromQuery] PromotionAnalyticsRangeQuery query,
            CancellationToken cancellationToken)
            => Ok(await _promotionService.GetAnalyticsAsync(id, query, cancellationToken));
    }
}
