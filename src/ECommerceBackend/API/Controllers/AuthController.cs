using System.Security.Claims;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ECommerceBackend.API.Controllers
{
    /// <summary>Xác thực người dùng — Đăng ký / Đăng nhập</summary>
    [ApiController]
    [Route("api/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService) => _authService = authService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        /// <summary>Đăng ký tài khoản mới (tự động gán role Customer)</summary>
        [HttpPost("register")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Register(
            [FromBody] RegisterRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RegisterAsync(request, cancellationToken);
            return Ok(result);
        }

        /// <summary>Đăng nhập và nhận JWT token</summary>
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Login(
            [FromBody] LoginRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.LoginAsync(request, cancellationToken);
            return Ok(result);
        }

        /// <summary>Yêu cầu gửi hướng dẫn đặt lại mật khẩu</summary>
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ForgotPassword(
            [FromBody] ForgotPasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.RequestPasswordResetAsync(request, cancellationToken);
            return Ok(new MessageResponse
            {
                Message = "Nếu email tồn tại, hướng dẫn đặt lại mật khẩu sẽ được gửi."
            });
        }

        /// <summary>Đặt lại mật khẩu bằng mã dùng một lần</summary>
        [HttpPost("reset-password")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.ResetPasswordAsync(request, cancellationToken);
            return Ok(new MessageResponse
            {
                Message = "Đặt lại mật khẩu thành công."
            });
        }

        /// <summary>Làm mới access token bằng refresh token</summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting("refresh")]
        [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Refresh(
            [FromBody] RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            var result = await _authService.RefreshAsync(request, cancellationToken);
            return Ok(result);
        }

        /// <summary>Đăng xuất và thu hồi refresh token hiện tại</summary>
        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> Logout(
            [FromBody] LogoutRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.LogoutAsync(CurrentUserId, request, cancellationToken);
            return Ok(new { message = "Đăng xuất thành công." });
        }

        /// <summary>Đăng xuất khỏi tất cả thiết bị và thu hồi toàn bộ phiên</summary>
        [HttpPost("logout-all")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
        {
            await _authService.LogoutAllAsync(CurrentUserId, cancellationToken);
            return Ok(new { message = "Đã đăng xuất khỏi tất cả thiết bị." });
        }
    }
}
