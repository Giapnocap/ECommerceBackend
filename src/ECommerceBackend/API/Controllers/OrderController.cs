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

        [HttpGet("my/summaries")]
        [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
        [ProducesResponseType(typeof(PagedResult<OrderSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyOrderSummaries(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
            CancellationToken cancellationToken = default)
        {
            var result = await _orderService.GetMyOrderSummariesAsync(
                CurrentUserId,
                page,
                pageSize,
                cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _orderService.GetByIdAsync(id, CurrentUserId, CanProcessOrders, cancellationToken);
            return Ok(result);
        }

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

        [HttpGet("summaries")]
        [Authorize(Policy = PermissionNames.ProcessOrders)]
        [ProducesResponseType(typeof(PagedResult<OrderSummaryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetOrderSummaries(
            [FromQuery] OrderQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _orderService.GetOrderSummariesAsync(
                queryParams,
                cancellationToken);
            return Ok(result);
        }

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
