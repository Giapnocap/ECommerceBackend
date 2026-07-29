using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý giỏ hàng</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/cart")]
    [Route("api/v{version:apiVersion}/cart")]
    [Authorize(Policy = AuthorizationPolicyNames.CustomerAccess)]
    [Produces("application/json")]
    public class CartController : ControllerBase
    {
        private readonly ICartService _cartService;
        public CartController(ICartService cartService) => _cartService = cartService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy giỏ hàng của tôi</summary>
        [HttpGet]
        [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyCart(CancellationToken cancellationToken)
        {
            var result = await _cartService.GetCartAsync(CurrentUserId, cancellationToken);
            return Ok(result);
        }

        /// <summary>Thêm sản phẩm vào giỏ hàng</summary>
        [HttpPost("items")]
        [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> AddItem(
            [FromBody] AddToCartRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.AddItemAsync(CurrentUserId, request, cancellationToken);
            return Ok(result);
        }

        /// <summary>Cập nhật số lượng sản phẩm trong giỏ (quantity = 0 để xóa)</summary>
        [HttpPut("items/{cartItemId:guid}")]
        [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateItem(
            Guid cartItemId,
            [FromBody] UpdateCartItemRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _cartService.UpdateItemAsync(CurrentUserId, cartItemId, request, cancellationToken);
            return Ok(result);
        }

        /// <summary>Xóa sản phẩm khỏi giỏ hàng</summary>
        [HttpDelete("items/{cartItemId:guid}")]
        [ProducesResponseType(typeof(CartResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RemoveItem(Guid cartItemId, CancellationToken cancellationToken)
        {
            var result = await _cartService.RemoveItemAsync(CurrentUserId, cartItemId, cancellationToken);
            return Ok(result);
        }

        /// <summary>Xóa toàn bộ giỏ hàng</summary>
        [HttpDelete]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ClearCart(CancellationToken cancellationToken)
        {
            await _cartService.ClearCartAsync(CurrentUserId, cancellationToken);
            return Ok(new { message = "Đã xóa toàn bộ giỏ hàng." });
        }
    }
}
