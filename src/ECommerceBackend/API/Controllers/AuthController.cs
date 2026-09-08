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
    [ApiController]
    [ApiVersion(1.0)]
    [Route("api/auth")]
    [Route("api/v{version:apiVersion}/auth")]
    [Produces("application/json")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        public AuthController(IAuthService authService) => _authService = authService;

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private Guid CurrentSessionId => Guid.Parse(
            User.FindFirstValue(AuthClaimTypes.SessionId)!);

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

        [HttpPost("email-verification")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> RequestEmailVerification(
            CancellationToken cancellationToken)
        {
            await _authService.RequestEmailVerificationAsync(
                CurrentUserId,
                cancellationToken);
            return Ok(new MessageResponse
            {
                Message = "Nếu email chưa được xác minh, liên kết mới sẽ được gửi."
            });
        }

        [HttpPost("email-verification/confirm")]
        [AllowAnonymous]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
        public async Task<IActionResult> ConfirmEmail(
            [FromBody] ConfirmEmailRequest request,
            CancellationToken cancellationToken)
        {
            await _authService.ConfirmEmailAsync(request, cancellationToken);
            return Ok(new MessageResponse
            {
                Message = "Xác minh email thành công."
            });
        }

        [HttpGet("sessions")]
        [Authorize]
        [ProducesResponseType(
            typeof(IReadOnlyList<AuthSessionResponse>),
            StatusCodes.Status200OK)]
        public async Task<IActionResult> GetSessions(
            CancellationToken cancellationToken)
            => Ok(await _authService.GetSessionsAsync(
                CurrentUserId,
                CurrentSessionId,
                cancellationToken));

        [HttpDelete("sessions/{sessionId:guid}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RevokeSession(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            await _authService.RevokeSessionAsync(
                CurrentUserId,
                sessionId,
                cancellationToken);
            return NoContent();
        }

        [HttpDelete("sessions")]
        [Authorize]
        [EnableRateLimiting("auth")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> RevokeAllSessions(
            CancellationToken cancellationToken)
        {
            await _authService.LogoutAllAsync(CurrentUserId, cancellationToken);
            return NoContent();
        }

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
