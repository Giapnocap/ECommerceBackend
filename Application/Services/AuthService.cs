using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthService : IAuthService
    {
        private readonly AuthRegistrationUseCase _registration;
        private readonly AuthSessionService _sessions;
        private readonly PasswordResetUseCase _passwordReset;

        public AuthService(
            AuthRegistrationUseCase registration,
            AuthSessionService sessions,
            PasswordResetUseCase passwordReset)
        {
            _registration = registration;
            _sessions = sessions;
            _passwordReset = passwordReset;
        }

        public Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
            => _registration.ExecuteAsync(request, cancellationToken);

        public Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
            => _sessions.LoginAsync(request, cancellationToken);

        public Task RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken = default)
            => _passwordReset.RequestAsync(request, cancellationToken);

        public Task ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default)
            => _passwordReset.ResetAsync(request, cancellationToken);

        public Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
            => _sessions.RefreshAsync(request, cancellationToken);

        public Task LogoutAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default)
            => _sessions.LogoutAsync(userId, request, cancellationToken);

        public Task LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _sessions.LogoutAllAsync(userId, cancellationToken);
    }
}
