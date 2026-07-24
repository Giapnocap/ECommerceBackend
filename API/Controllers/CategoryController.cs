using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý danh mục sản phẩm</summary>
    [ApiController]
    [Route("api/categories")]
    [Produces("application/json")]
    public class CategoryController : ControllerBase
    {
        private readonly ICategoryService _categoryService;
        public CategoryController(ICategoryService categoryService) => _categoryService = categoryService;
        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy tất cả danh mục (dạng cây cha-con)</summary>
        [HttpGet]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IEnumerable<CategoryResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
        {
            var result = await _categoryService.GetAllAsync(cancellationToken);
            return Ok(result);
        }

        /// <summary>Lấy chi tiết danh mục theo Id</summary>
        [HttpGet("{id:guid}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
        {
            var result = await _categoryService.GetByIdAsync(id, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Tạo danh mục mới</summary>
        [HttpPost]
        [Authorize(Policy = PermissionNames.ManageCategories)]
        [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status201Created)]
        public async Task<IActionResult> Create(
            [FromBody] CreateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _categoryService.CreateAsync(
                request,
                CurrentUserId,
                cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }

        /// <summary>[Admin] Cập nhật danh mục</summary>
        [HttpPut("{id:guid}")]
        [Authorize(Policy = PermissionNames.ManageCategories)]
        [ProducesResponseType(typeof(CategoryResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _categoryService.UpdateAsync(
                id,
                request,
                CurrentUserId,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Xóa danh mục (soft delete)</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = PermissionNames.ManageCategories)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _categoryService.DeleteAsync(id, CurrentUserId, cancellationToken);
            return Ok(new { message = "Xóa danh mục thành công." });
        }
    }
}
