using System.Security.Claims;
using Asp.Versioning;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý người dùng</summary>
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/users")]
    [Route("api/v{version:apiVersion}/users")]
    [Authorize]
    [Produces("application/json")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        public UserController(IUserService userService) => _userService = userService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy thông tin hồ sơ của chính mình</summary>
        [HttpGet("me")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyProfile(CancellationToken cancellationToken)
        {
            var result = await _userService.GetProfileAsync(CurrentUserId, cancellationToken);
            return Ok(result);
        }

        /// <summary>Cập nhật hồ sơ cá nhân</summary>
        [HttpPut("me")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateMyProfile(
            [FromBody] UpdateProfileRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _userService.UpdateProfileAsync(
                CurrentUserId,
                request,
                cancellationToken);
            return Ok(result);
        }

        /// <summary>Đổi mật khẩu</summary>
        [HttpPut("me/change-password")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword(
            [FromBody] ChangePasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _userService.ChangePasswordAsync(CurrentUserId, request, cancellationToken);
            return Ok(new { message = "Đổi mật khẩu thành công." });
        }

        /// <summary>[Admin] Lấy danh sách tất cả người dùng</summary>
        [HttpGet]
        [Authorize(Policy = PermissionNames.ManageUsers)]
        [ProducesResponseType(typeof(PagedResult<UserResponse>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAllUsers(
            [FromQuery] UserQueryParams queryParams,
            CancellationToken cancellationToken)
        {
            var result = await _userService.GetAllUsersAsync(queryParams, cancellationToken);
            return Ok(result);
        }

        /// <summary>[Admin] Gán vai trò cho người dùng</summary>
        [HttpPut("{id:guid}/role")]
        [Authorize(Policy = PermissionNames.ManageUsers)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> AssignRole(
            Guid id,
            [FromBody] AssignRoleRequest request,
            CancellationToken cancellationToken)
        {
            await _userService.AssignRoleAsync(
                CurrentUserId,
                id,
                request,
                cancellationToken);
            return Ok(new { message = $"Đã gán vai trò '{request.RoleName}' cho người dùng." });
        }
    }
}
