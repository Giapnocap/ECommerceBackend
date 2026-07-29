using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý đơn hàng</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/orders")]
    [Route("api/v{version:apiVersion}/orders")]
    [Authorize]
    [Produces("application/json")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        public OrderController(IOrderService orderService) => _orderService = orderService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool CanProcessOrders => User.HasClaim(AuthClaimTypes.Permission, PermissionNames.ProcessOrders);

        /// <summary>Đặt hàng từ giỏ hàng hiện tại trong một giao dịch dữ liệu</summary>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [EnableRateLimiting("checkout")]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> PlaceOrder(
            [FromBody] PlaceOrderRequest request,
            [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.PlaceOrderAsync(
                CurrentUserId,
                request,
                idempotencyKey,
                cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        /// <summary>Tính lại giá giỏ hàng theo khuyến mãi và phương thức giao hàng</summary>
        [HttpPost("quote")]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [EnableRateLimiting("checkout")]
        [ProducesResponseType(typeof(OrderQuoteResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetQuote(
            [FromBody] OrderQuoteRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.GetQuoteAsync(
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>Lấy danh sách đơn hàng của tôi</summary>
        [HttpGet("my")]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [ProducesResponseType(typeof(PagedResult<OrderResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyOrders(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            var result = await _orderService.GetMyOrdersAsync(CurrentUserId, page, pageSize, cancellationToken);
            return Ok(result);
        }

        /// <summary>Lấy chi tiết đơn hàng theo Id</summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _orderService.GetByIdAsync(id, CurrentUserId, CanProcessOrders, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Lấy tất cả đơn hàng theo bộ lọc</summary>
        [HttpGet]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(PagedResult<OrderResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllOrders(
            [FromQuery] OrderQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.GetAllOrdersAsync(queryParams, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Cập nhật trạng thái đơn hàng</summary>
        [HttpPut("{id:guid}/status")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateStatus(
            Guid id,
            [FromBody] UpdateOrderStatusRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.UpdateStatusAsync(id, CurrentUserId, request, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Tạo vận đơn và xuất giao đơn hàng</summary>
        /// <param name="id">Mã đơn hàng</param>
        /// <param name="request">Thông tin đơn vị vận chuyển và mã vận đơn</param>
        /// <param name="cancellationToken">Token hủy yêu cầu</param>
        [HttpPost("{id:guid}/shipment/dispatch")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> DispatchShipment(
            Guid id,
            [FromBody] DispatchShipmentRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.DispatchShipmentAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Xác nhận vận đơn đã giao thành công</summary>
        /// <param name="id">Mã đơn hàng</param>
        /// <param name="request">Ghi chú giao hàng thành công</param>
        /// <param name="cancellationToken">Token hủy yêu cầu</param>
        [HttpPost("{id:guid}/shipment/deliver")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> MarkShipmentDelivered(
            Guid id,
            [FromBody] MarkShipmentDeliveredRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.MarkShipmentDeliveredAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>Khách hàng yêu cầu trả một đơn đã giao</summary>
        /// <param name="id">Mã đơn hàng thuộc khách hàng hiện tại</param>
        /// <param name="request">Lý do trả hàng</param>
        /// <param name="cancellationToken">Token hủy yêu cầu</param>
        [HttpPost("{id:guid}/return-request")]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RequestReturn(
            Guid id,
            [FromBody] CreateReturnRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.RequestReturnAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Duyệt hoặc từ chối yêu cầu trả hàng</summary>
        /// <param name="id">Mã đơn hàng</param>
        /// <param name="request">Quyết định và ghi chú xét duyệt</param>
        /// <param name="cancellationToken">Token hủy yêu cầu</param>
        [HttpPost("{id:guid}/return-request/review")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ReviewReturn(
            Guid id,
            [FromBody] ReviewReturnRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.ReviewReturnAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Nhận và kiểm tra hàng hoàn</summary>
        /// <param name="id">Mã đơn hàng</param>
        /// <param name="request">Ghi chú kiểm tra hàng hoàn</param>
        /// <param name="cancellationToken">Token hủy yêu cầu</param>
        [HttpPost("{id:guid}/return-request/receive")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ReceiveReturn(
            Guid id,
            [FromBody] ReceiveReturnRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.ReceiveReturnAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Staff/Admin] Ghi nhận hoàn tiền COD đã hoàn tất cho đơn hoàn hàng</summary>
        [HttpPost("{id:guid}/refund")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RecordRefund(
            Guid id,
            [FromBody] RecordOrderRefundRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.RecordRefundAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>Hủy một đơn hàng hợp lệ của khách hàng hiện tại</summary>
        [HttpPost("{id:guid}/cancel")]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Cancel(
            Guid id,
            [FromBody] CancelOrderRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.CancelByCustomerAsync(
                id,
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }
    }
}
