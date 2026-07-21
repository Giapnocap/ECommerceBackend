using System.Security.Claims;
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
    [Route("api/orders")]
    [Authorize]
    [Produces("application/json")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;
        public OrderController(IOrderService orderService) => _orderService = orderService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool CanProcessOrders => User.HasClaim(AuthClaimTypes.Permission, PermissionNames.ProcessOrders);

        /// <summary>Đặt hàng từ giỏ hàng hiện tại (có Transaction)</summary>
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

        /// <summary>[Staff/Admin] Lấy tất cả đơn hàng với filter</summary>
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

        /// <summary>Cancel one of the current customer's eligible orders</summary>
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
