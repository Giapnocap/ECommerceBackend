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
    /// <summary>Quản lý sản phẩm</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/products")]
    [Route("api/v{version:apiVersion}/products")]
    [Produces("application/json")]
    public class ProductController : ControllerBase
    {
        private readonly IProductService _productService;
        private readonly IUploadService _uploadService;
        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        public ProductController(
            IProductService productService,
            IUploadService uploadService)
        {
            _productService = productService;
            _uploadService = uploadService;
        }

        /// <summary>Lấy danh sách sản phẩm có phân trang, lọc và sắp xếp</summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(PagedResult<ProductResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(
            [FromQuery] ProductQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _productService.GetAllAsync(queryParams, cancellationToken);
            return Ok(result);
        }

        /// <summary>Lấy chi tiết sản phẩm theo Id</summary>
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _productService.GetByIdAsync(id, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Tạo sản phẩm mới</summary>
        [HttpPost]
        [Authorize(Policy = PermissionNames.ManageProducts)]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create(
            [FromBody] CreateProductRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _productService.CreateAsync(
                request,
                CurrentUserId,
                cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        /// <summary>[Admin] Cập nhật sản phẩm</summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = PermissionNames.ManageProducts)]
        [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateProductRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _productService.UpdateAsync(
                id,
                request,
                CurrentUserId,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Xóa mềm sản phẩm</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = PermissionNames.ManageProducts)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _productService.DeleteAsync(id, CurrentUserId, cancellationToken);
            return Ok(new { message = "Xóa sản phẩm thành công." });
        }

        /// <summary>[Admin] Tải ảnh lên cho sản phẩm</summary>
        [HttpPost("{id:guid}/images")]
        [Authorize(Policy = PermissionNames.ManageProducts)]
        [EnableRateLimiting("upload")]
        [Consumes("multipart/form-data")]
        [ProducesResponseType(typeof(UploadImageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UploadImage(
            Guid id,
            IFormFile file,
            [FromQuery] bool isMain = false,
            CancellationToken cancellationToken = default)
        {
            var result = await _uploadService.UploadProductImageAsync(
                id,
                file,
                isMain,
                cancellationToken,
                CurrentUserId);
            return Ok(result);
        }

        /// <summary>[Admin] Xóa ảnh sản phẩm</summary>
        [HttpDelete("{id:guid}/images/{imageId:guid}")]
        [Authorize(Policy = PermissionNames.ManageProducts)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> DeleteImage(
            Guid id,
            Guid imageId,
            CancellationToken cancellationToken = default)
        {
            await _uploadService.DeleteProductImageAsync(
                id,
                imageId,
                cancellationToken,
                CurrentUserId);
            return Ok(new { message = "Xóa ảnh thành công." });
        }
    }
}
