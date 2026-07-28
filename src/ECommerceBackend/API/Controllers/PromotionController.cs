using System.Security.Claims;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý mã khuyến mãi</summary>
    [ApiController]
    [Route("api/promotions")]
    [Authorize(Policy = PermissionNames.ManageProducts)]
    [Produces("application/json")]
    public sealed class PromotionController : ControllerBase
    {
        private readonly IPromotionService _promotionService;

        public PromotionController(IPromotionService promotionService)
        {
            _promotionService = promotionService;
        }

        private Guid CurrentUserId
            => Guid.Parse(
                User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>[Admin] Lấy danh sách mã khuyến mãi</summary>
        [HttpGet]
        [ProducesResponseType(
            typeof(PagedResult<PromotionResponse>),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(
            [FromQuery] PromotionQueryParams query,
            CancellationToken cancellationToken)
        {
            var result = await _promotionService.GetAllAsync(
                query,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Lấy chi tiết mã khuyến mãi</summary>
        [HttpGet("{id:guid}")]
        [ProducesResponseType(
            typeof(PromotionResponse),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetById(
            Guid id,
            CancellationToken cancellationToken)
        {
            var result = await _promotionService.GetByIdAsync(
                id,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Tạo mã khuyến mãi</summary>
        [HttpPost]
        [ProducesResponseType(
            typeof(PromotionResponse),
            StatusCodes.Status201Created)]
        public async Task<IActionResult> Create(
            [FromBody] CreatePromotionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _promotionService.CreateAsync(
                request,
                CurrentUserId,
                cancellationToken);
            return CreatedAtAction(
                nameof(GetById),
                new { id = result.Id },
                result);
        }

        /// <summary>[Admin] Cập nhật chính sách mã khuyến mãi</summary>
        [HttpPut("{id:guid}")]
        [ProducesResponseType(
            typeof(PromotionResponse),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> Update(
            Guid id,
            [FromBody] UpdatePromotionRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _promotionService.UpdateAsync(
                id,
                request,
                CurrentUserId,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Ngừng sử dụng mã khuyến mãi</summary>
        [HttpDelete("{id:guid}")]
        [ProducesResponseType(
            typeof(MessageResponse),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> Deactivate(
            Guid id,
            CancellationToken cancellationToken)
        {
            await _promotionService.DeactivateAsync(
                id,
                CurrentUserId,
                cancellationToken);
            return Ok(new MessageResponse
            {
                Message = "Đã ngừng sử dụng mã khuyến mãi."
            });
        }
    }
}
