using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthService : IAuthService
    {
        private readonly AuthRegistrationUseCase _registration;
        private readonly AuthLoginUseCase _login;
        private readonly AuthRefreshUseCase _refresh;
        private readonly AuthLogoutUseCase _logout;
        private readonly AuthLogoutAllUseCase _logoutAll;
        private readonly PasswordResetUseCase _passwordReset;

        public AuthService(
            AuthRegistrationUseCase registration,
            AuthLoginUseCase login,
            AuthRefreshUseCase refresh,
            AuthLogoutUseCase logout,
            AuthLogoutAllUseCase logoutAll,
            PasswordResetUseCase passwordReset)
        {
            _registration = registration;
            _login = login;
            _refresh = refresh;
            _logout = logout;
            _logoutAll = logoutAll;
            _passwordReset = passwordReset;
        }

        public Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
            => _registration.ExecuteAsync(request, cancellationToken);

        public Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
            => _login.ExecuteAsync(request, cancellationToken);

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
            => _refresh.ExecuteAsync(request, cancellationToken);

        public Task LogoutAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default)
            => _logout.ExecuteAsync(userId, request, cancellationToken);

        public Task LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _logoutAll.ExecuteAsync(userId, cancellationToken);
    }
}
