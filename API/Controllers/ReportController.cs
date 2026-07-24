using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Administrative commerce reports</summary>
    [ApiController]
    [Route("api/reports")]
    [Authorize(Policy = PermissionNames.ViewReports)]
    [Produces("application/json")]
    public sealed class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        /// <summary>Lấy báo cáo tổng quan theo khoảng thời gian [From, To)</summary>
        /// <remarks>
        /// TotalOrders và OrdersByStatus tính theo thời điểm tạo đơn.
        /// DeliveredOrders, CancelledOrders và TopSellingProducts tính theo thời điểm chuyển trạng thái tương ứng.
        /// Doanh thu tính theo thời điểm thanh toán và hoàn tiền.
        /// </remarks>
        [HttpGet("sales-summary")]
        [ProducesResponseType(typeof(SalesSummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSalesSummary(
            [FromQuery] SalesSummaryQuery query,
            CancellationToken cancellationToken)
        {
            var result = await _reportService.GetSalesSummaryAsync(query, cancellationToken);
            return Ok(result);
        }
    }
}
