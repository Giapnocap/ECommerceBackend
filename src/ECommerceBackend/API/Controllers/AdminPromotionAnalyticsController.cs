using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Phân tích hiệu quả mã khuyến mãi dành cho quản trị viên</summary>
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

        /// <summary>Xếp hạng hiệu quả các mã khuyến mãi</summary>
        /// <remarks>
        /// from và to lọc theo thời điểm đổi mã. GrossRevenue là tổng tạm tính đơn hàng,
        /// NetRevenue là tổng tạm tính trừ giảm giá; hai chỉ số không đại diện cho tiền đã thu,
        /// phí vận chuyển, thuế hoặc số tiền hoàn.
        /// </remarks>
        [HttpGet("analytics")]
        [ProducesResponseType(
            typeof(PagedResult<PromotionAnalyticsResponse>),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalytics(
            [FromQuery] PromotionAnalyticsQuery query,
            CancellationToken cancellationToken)
            => Ok(await _promotionService.GetAnalyticsAsync(query, cancellationToken));

        /// <summary>Xem hiệu quả của một mã khuyến mãi</summary>
        [HttpGet("{id:guid}/analytics")]
        [ProducesResponseType(typeof(PromotionAnalyticsResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAnalyticsByPromotion(
            Guid id,
            [FromQuery] PromotionAnalyticsRangeQuery query,
            CancellationToken cancellationToken)
            => Ok(await _promotionService.GetAnalyticsAsync(id, query, cancellationToken));
    }
}
