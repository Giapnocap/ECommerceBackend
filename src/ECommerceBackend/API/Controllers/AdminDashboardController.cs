using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Tổng quan vận hành và kinh doanh dành cho quản trị viên</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/admin/dashboard")]
    [Route("api/v{version:apiVersion}/admin/dashboard")]
    [Authorize(Policy = PermissionNames.ViewReports)]
    [Produces("application/json")]
    public sealed class AdminDashboardController : ControllerBase
    {
        private readonly IAdminDashboardService _dashboardService;

        public AdminDashboardController(IAdminDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        /// <summary>Lấy các chỉ số vận hành chính tại thời điểm hiện tại</summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSummary(
            [FromQuery] DashboardSummaryQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetSummaryAsync(query, cancellationToken));

        /// <summary>Lấy xu hướng doanh thu thực thu theo ngày, tuần hoặc tháng</summary>
        [HttpGet("revenue")]
        [ProducesResponseType(typeof(DashboardRevenueTrendResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRevenue(
            [FromQuery] DashboardRevenueQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetRevenueAsync(query, cancellationToken));

        /// <summary>Lấy phân bố trạng thái đơn hàng hiện tại</summary>
        [HttpGet("orders-by-status")]
        [ProducesResponseType(typeof(IReadOnlyList<StatusBreakdownResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrdersByStatus(CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetOrdersByStatusAsync(cancellationToken));

        /// <summary>Lấy các sản phẩm bán chạy theo đơn đã giao trong khoảng thời gian yêu cầu</summary>
        [HttpGet("top-products")]
        [ProducesResponseType(typeof(IReadOnlyList<TopSellingProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTopProducts(
            [FromQuery] DashboardTopProductsQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetTopProductsAsync(query, cancellationToken));

        /// <summary>Lấy các sản phẩm có tồn kho không vượt quá ngưỡng</summary>
        [HttpGet("low-stock")]
        [ProducesResponseType(typeof(PagedResult<LowStockProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLowStock(
            [FromQuery] LowStockQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetLowStockAsync(query, cancellationToken));

        /// <summary>Lấy hoạt động gần đây đã được giới hạn từ đơn hàng, trả hàng và audit</summary>
        [HttpGet("recent-activities")]
        [ProducesResponseType(typeof(IReadOnlyList<DashboardRecentActivityResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRecentActivities(
            [FromQuery] DashboardRecentActivitiesQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetRecentActivitiesAsync(query, cancellationToken));
    }
}
