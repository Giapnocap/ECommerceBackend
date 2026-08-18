using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default);
        Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default);
        Task RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken = default);
        Task ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default);
        Task RequestEmailVerificationAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
        Task ConfirmEmailAsync(
            ConfirmEmailRequest request,
            CancellationToken cancellationToken = default);
        Task<IReadOnlyList<AuthSessionResponse>> GetSessionsAsync(
            Guid userId,
            Guid currentSessionId,
            CancellationToken cancellationToken = default);
        Task RevokeSessionAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default);
        Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default);
        Task LogoutAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default);
        Task LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
