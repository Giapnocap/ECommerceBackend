using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Báo cáo phân tích dành cho quản trị viên</summary>
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

        /// <summary>Lấy doanh thu theo thời điểm thanh toán và hoàn tiền</summary>
        /// <remarks>
        /// GrossRevenue tính từ payment có PaidAt trong [from, to) và trạng thái hiện tại Paid hoặc Refunded.
        /// RefundAmount tính từ payment-status history chuyển sang Refunded trong [from, to).
        /// AverageOrderValue là GrossRevenue chia cho số order có payment được tính vào GrossRevenue.
        /// </remarks>
        [HttpGet("revenue")]
        [ProducesResponseType(typeof(RevenueReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetRevenue(
            [FromQuery] RevenueReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetRevenueReportAsync(query, cancellationToken));

        /// <summary>Lấy thống kê đơn hàng theo cohort ngày tạo đơn</summary>
        /// <remarks>
        /// Các trạng thái và tỷ lệ được tính từ trạng thái hiện tại của order được tạo trong [from, to).
        /// ReturnedOrders gồm trạng thái Returned và Refunded.
        /// </remarks>
        [HttpGet("orders")]
        [ProducesResponseType(typeof(OrderReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrders(
            [FromQuery] OrderReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetOrderReportAsync(query, cancellationToken));

        /// <summary>Lấy sản phẩm bán chạy theo thời điểm chuyển sang Delivered và số lượng tồn thấp hiện tại</summary>
        [HttpGet("products")]
        [ProducesResponseType(typeof(ProductReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] ProductReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetProductReportAsync(query, cancellationToken));

        /// <summary>Lấy chỉ số khách hàng và khách hàng chi tiêu cao nhất</summary>
        /// <remarks>
        /// TopCustomers xếp theo tiền đã thanh toán trừ hoàn tiền trong [from, to).
        /// </remarks>
        [HttpGet("customers")]
        [ProducesResponseType(typeof(CustomerReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetCustomers(
            [FromQuery] CustomerReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetCustomerReportAsync(query, cancellationToken));

        /// <summary>Lấy chỉ số yêu cầu trả hàng, hoàn tiền và các lý do phổ biến</summary>
        /// <remarks>
        /// ReturnRequestCount tính theo RequestedAt; ReturnRatePercent lấy số yêu cầu trả hàng chia cho order tạo trong [from, to).
        /// RefundAmount tính theo thời điểm payment được chuyển sang Refunded.
        /// </remarks>
        [HttpGet("returns")]
        [ProducesResponseType(typeof(ReturnReportResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetReturns(
            [FromQuery] ReturnReportQuery query,
            CancellationToken cancellationToken)
            => Ok(await _reportService.GetReturnReportAsync(query, cancellationToken));
    }
}
