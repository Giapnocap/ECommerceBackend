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
    [Route("api/reports")]
    [Route("api/v{version:apiVersion}/reports")]
    [Authorize(Policy = PermissionNames.ViewReports)]
    [Produces("application/json")]
    public sealed class ReportController : ControllerBase
    {
        private readonly IReportService _reportService;

        public ReportController(IReportService reportService)
        {
            _reportService = reportService;
        }

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
