using System.Security.Claims;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Quản lý người dùng</summary>
    [ApiController]
    [Route("api/users")]
    [Authorize]
    [Produces("application/json")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;
        public UserController(IUserService userService) => _userService = userService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Lấy thông tin profile của chính mình</summary>
        [HttpGet("me")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetMyProfile()
        {
            var result = await _userService.GetProfileAsync(CurrentUserId);
            return Ok(result);
        }

        /// <summary>Cập nhật profile cá nhân</summary>
        [HttpPut("me")]
        [ProducesResponseType(typeof(UserResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> UpdateMyProfile([FromBody] UpdateProfileRequest request)
        {
            var result = await _userService.UpdateProfileAsync(CurrentUserId, request);
            return Ok(result);
        }

        /// <summary>Đổi mật khẩu</summary>
        [HttpPut("me/change-password")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            await _userService.ChangePasswordAsync(CurrentUserId, request);
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

        /// <summary>[Admin] Gán role cho người dùng</summary>
        [HttpPut("{id:guid}/role")]
        [Authorize(Policy = PermissionNames.ManageUsers)]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> AssignRole(Guid id, [FromBody] AssignRoleRequest request)
        {
            await _userService.AssignRoleAsync(CurrentUserId, id, request);
            return Ok(new { message = $"Đã gán role '{request.RoleName}' cho người dùng." });
        }
    }
}
