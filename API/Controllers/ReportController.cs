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

        /// <summary>Lấy báo cáo tổng quan doanh thu, đơn hàng và sản phẩm bán chạy</summary>
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
