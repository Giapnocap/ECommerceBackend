using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý vận hành tồn kho dành cho quản trị viên</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/admin/inventory")]
    [Route("api/v{version:apiVersion}/admin/inventory")]
    [Authorize(Policy = PermissionNames.ManageProducts)]
    [Produces("application/json")]
    public sealed class AdminInventoryController : ControllerBase
    {
        private readonly IInventoryService _inventoryService;
        private readonly IProductService _productService;

        public AdminInventoryController(
            IInventoryService inventoryService,
            IProductService productService)
        {
            _inventoryService = inventoryService;
            _productService = productService;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy danh sách tồn kho có tìm kiếm, lọc và phân trang</summary>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResult<InventoryProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetProducts(
            [FromQuery] InventoryProductQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _inventoryService.GetProductsAsync(query, cancellationToken));

        /// <summary>Nhập thêm hàng và ghi nhận biến động tồn kho nguyên tử</summary>
        [HttpPost("{productId:guid}/stock-in")]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
        public async Task<IActionResult> StockIn(
            Guid productId,
            [FromBody] StockInRequest request,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            CancellationToken cancellationToken)
        {
            var result = await _productService.StockInAsync(
                productId,
                request,
                ParseRequiredVersion(ifMatch),
                CurrentUserId,
                cancellationToken);
            SetProductEtag(result);
            return Ok(result);
        }

        /// <summary>Điều chỉnh tồn kho sau kiểm kê hoặc xử lý chênh lệch</summary>
        [HttpPost("{productId:guid}/adjust")]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
        public async Task<IActionResult> Adjust(
            Guid productId,
            [FromBody] AdjustProductStockRequest request,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            CancellationToken cancellationToken)
        {
            var result = await _productService.AdjustStockAsync(
                productId,
                request,
                ParseRequiredVersion(ifMatch),
                CurrentUserId,
                cancellationToken);
            SetProductEtag(result);
            return Ok(result);
        }

        /// <summary>Lấy lịch sử biến động tồn kho đã lọc và phân trang</summary>
        [HttpGet("{productId:guid}/history")]
        [ProducesResponseType(typeof(PagedResult<InventoryTransactionResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetHistory(
            Guid productId,
            [FromQuery] InventoryQueryParams query,
            CancellationToken cancellationToken)
            => Ok(await _inventoryService.GetTransactionsAsync(
                productId,
                query,
                cancellationToken));

        /// <summary>Cập nhật ngưỡng cảnh báo tồn kho riêng của sản phẩm</summary>
        [HttpPut("{productId:guid}/threshold")]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
        public async Task<IActionResult> UpdateThreshold(
            Guid productId,
            [FromBody] UpdateLowStockThresholdRequest request,
            [FromHeader(Name = "If-Match")] string? ifMatch,
            CancellationToken cancellationToken)
        {
            var result = await _productService.UpdateLowStockThresholdAsync(
                productId,
                request,
                ParseRequiredVersion(ifMatch),
                CurrentUserId,
                cancellationToken);
            SetProductEtag(result);
            return Ok(result);
        }

        private static byte[] ParseRequiredVersion(string? ifMatch)
        {
            if (string.IsNullOrWhiteSpace(ifMatch))
            {
                throw new ApiException(
                    StatusCodes.Status428PreconditionRequired,
                    "precondition_required",
                    "Header If-Match chứa phiên bản tồn kho là bắt buộc.");
            }

            var candidate = ifMatch.Trim();
            if (candidate.Length < 3
                || candidate[0] != '"'
                || candidate[^1] != '"'
                || candidate.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
                || candidate.Contains(','))
            {
                throw InvalidIfMatch();
            }

            try
            {
                var version = Convert.FromBase64String(candidate[1..^1]);
                if (version.Length == 0)
                    throw InvalidIfMatch();

                return version;
            }
            catch (FormatException)
            {
                throw InvalidIfMatch();
            }
        }

        private static ApiException InvalidIfMatch()
            => new(
                StatusCodes.Status400BadRequest,
                "invalid_if_match",
                "Header If-Match phải là ETag mạnh chứa phiên bản sản phẩm hợp lệ.");

        private void SetProductEtag(ProductResponse product)
        {
            if (!string.IsNullOrWhiteSpace(product.Version))
                Response.Headers.ETag = $"\"{product.Version}\"";
        }
    }
}
