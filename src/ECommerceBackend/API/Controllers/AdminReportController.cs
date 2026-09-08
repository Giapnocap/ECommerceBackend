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
    [Route("api/admin/reports")]
    [Route("api/v{version:apiVersion}/admin/reports")]
    [Authorize(Policy = PermissionNames.ViewReports)]
    [Produces("application/json")]
    public sealed class AdminReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public AdminReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

        [HttpGet("revenue")]
        [ProducesResponseType(typeof(RevenueReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRevenue(
            [FromQuery] RevenueReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetRevenueReportAsync(query, cancellationToken));

        [HttpGet("orders")]
        [ProducesResponseType(typeof(OrderReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrders(
            [FromQuery] OrderReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetOrderReportAsync(query, cancellationToken));

        [HttpGet("products")]
        [ProducesResponseType(typeof(ProductReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] ProductReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetProductReportAsync(query, cancellationToken));

        [HttpGet("customers")]
        [ProducesResponseType(typeof(CustomerReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomers(
            [FromQuery] CustomerReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetCustomerReportAsync(query, cancellationToken));

        [HttpGet("returns")]
        [ProducesResponseType(typeof(ReturnReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReturns(
            [FromQuery] ReturnReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetReturnReportAsync(query, cancellationToken));
    }
}
