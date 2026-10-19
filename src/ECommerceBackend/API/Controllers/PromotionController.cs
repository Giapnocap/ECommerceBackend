using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/v{version:apiVersion}/promotions")]
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

        [HttpGet("available")]
        [AllowAnonymous]
        [ProducesResponseType(
            typeof(IReadOnlyList<PromotionResponse>),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAvailable(
            CancellationToken cancellationToken)
        {
            var result = await _promotionService.GetAllAsync(
                new PromotionQueryParams { IsActive = true, Page = 1, PageSize = 100 },
                cancellationToken);

            var now = DateTime.UtcNow;
            var activePromotions = result.Items
                .Where(p => p.IsActive && p.StartsAt <= now && p.EndsAt >= now && p.UsedCount < p.UsageLimit)
                .OrderByDescending(p => p.Value)
                .ToList();

            return Ok(activePromotions);
        }

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
