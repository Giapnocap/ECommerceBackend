using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    [Authorize(Policy = PermissionNames.ViewInventory)]
    [Produces("application/json")]
    public sealed class InventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;

        public InventoryController(IInventoryService inventoryService)
        {
            _inventoryService = inventoryService;
        }

        /// <summary>Lấy lịch sử biến động tồn kho của một sản phẩm</summary>
        [HttpGet("products/{productId:guid}/transactions")]
        [ProducesResponseType(typeof(PagedResult<InventoryTransactionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetTransactions(
            Guid productId,
            [FromQuery] InventoryQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _inventoryService.GetTransactionsAsync(
                productId,
                queryParams,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>Lấy danh sách sản phẩm sắp hết hàng</summary>
        [HttpGet("low-stock")]
        [ProducesResponseType(typeof(PagedResult<LowStockProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetLowStock(
            [FromQuery] LowStockQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _inventoryService.GetLowStockAsync(queryParams, cancellationToken);
            return Ok(result);
        }
    }
}
