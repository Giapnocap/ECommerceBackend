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

        [HttpGet("summary")]
        [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSummary(
            [FromQuery] DashboardSummaryQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetSummaryAsync(query, cancellationToken));

        [HttpGet("revenue")]
        [ProducesResponseType(typeof(DashboardRevenueTrendResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRevenue(
            [FromQuery] DashboardRevenueQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetRevenueAsync(query, cancellationToken));

        [HttpGet("orders-by-status")]
        [ProducesResponseType(typeof(IReadOnlyList<StatusBreakdownResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrdersByStatus(CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetOrdersByStatusAsync(cancellationToken));

        [HttpGet("top-products")]
        [ProducesResponseType(typeof(IReadOnlyList<TopSellingProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTopProducts(
            [FromQuery] DashboardTopProductsQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetTopProductsAsync(query, cancellationToken));

        [HttpGet("low-stock")]
        [ProducesResponseType(typeof(PagedResult<LowStockProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLowStock(
            [FromQuery] LowStockQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetLowStockAsync(query, cancellationToken));

        [HttpGet("recent-activities")]
        [ProducesResponseType(typeof(IReadOnlyList<DashboardRecentActivityResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRecentActivities(
            [FromQuery] DashboardRecentActivitiesQuery query,
            CancellationToken cancellationToken)
            => Ok(await _dashboardService.GetRecentActivitiesAsync(query, cancellationToken));
    }
}
