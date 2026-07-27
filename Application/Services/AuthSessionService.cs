using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthSessionService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IAuditWriter _audit;
        private readonly AuthTokenIssuer _tokenIssuer;
        private readonly AuthSecurityOptions _securityOptions;
        private readonly TimeProvider _timeProvider;

        public AuthSessionService(
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            AuthTokenIssuer tokenIssuer,
            IOptions<AuthSecurityOptions> securityOptions,
            IPasswordHasher passwordHasher,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _passwordHasher = passwordHasher;
            _audit = audit;
            _tokenIssuer = tokenIssuer;
            _securityOptions = securityOptions.Value;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.login", cancellationToken);
            var normalizedUserName = Normalize(request.UserName);
            var userId =
                await _userRepository.GetActiveUserIdByUserNameAsync(
                    normalizedUserName,
                    cancellationToken);
            if (!userId.HasValue)
            {
                _ = _passwordHasher.Verify(request.Password, null);
                throw Unauthorized();
            }

            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId.Value,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw Unauthorized();

                var occurredAt = UtcNow;
                var passwordValid = _passwordHasher.Verify(
                    request.Password,
                    user.PasswordHash);
                if (user.IsLockedOutAt(occurredAt))
                {
                    telemetry.SetTag("auth.account.locked", true);
                    throw Unauthorized();
                }

                if (!passwordValid)
                {
                    var locked = DomainRuleGuard.AsConflict(() =>
                        user.RecordFailedLogin(
                            occurredAt,
                            _securityOptions.MaxFailedLoginAttempts,
                            TimeSpan.FromMinutes(_securityOptions.LockoutMinutes)));
                    if (locked)
                    {
                        telemetry.SetTag("auth.account.locked", true);
                        _audit.Write(
                            "auth.account.locked",
                            nameof(User),
                            user.Id.ToString(),
                            metadata: new Dictionary<string, object?>
                            {
                                ["lockoutMinutes"] = _securityOptions.LockoutMinutes,
                                ["failedAttempts"] = user.FailedLoginCount
                            });
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    throw Unauthorized();
                }

                user.ClearLoginFailures();
                await LoadRolesAndPermissionsAsync(user, cancellationToken);
                var refreshToken = _tokenIssuer.CreateRefreshToken(
                    user.Id,
                    Guid.NewGuid(),
                    occurredAt);
                await _authSessionRepository.AddRefreshTokenAsync(
                    refreshToken.Entity,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = _tokenIssuer.BuildResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    refreshToken.RawToken,
                    refreshToken.Entity.ExpiresAt,
                    refreshToken.Entity.FamilyId,
                    occurredAt);
                telemetry.Complete();
                return response;
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.refresh", cancellationToken);
            var tokenHash = AuthTokenIssuer.HashRefreshToken(
                request.RefreshToken);
            var tokenOwnerId = await FindRefreshTokenOwnerIdAsync(tokenHash, cancellationToken)
                ?? throw Unauthorized();
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    tokenOwnerId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw Unauthorized();
                var storedToken = await LoadRefreshTokenForUpdateAsync(tokenHash, cancellationToken)
                    ?? throw Unauthorized();
                if (storedToken.UserId != user.Id)
                    throw Unauthorized();

                var occurredAt = UtcNow;
                if (storedToken.RevokedAt.HasValue)
                {
                    if (!string.IsNullOrWhiteSpace(storedToken.ReplacedByTokenHash))
                    {
                        await RevokeTokenFamilyAsync(
                            storedToken.UserId,
                            storedToken.FamilyId,
                            "Refresh token reuse detected",
                            occurredAt,
                            cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        transactionCompleted = true;
                    }

                    throw Unauthorized();
                }

                if (storedToken.IsExpiredAt(occurredAt))
                    throw Unauthorized();

                await LoadRolesAndPermissionsAsync(user, cancellationToken);
                var newRefreshToken = _tokenIssuer.CreateRefreshToken(
                    user.Id,
                    storedToken.FamilyId,
                    occurredAt);
                DomainRuleGuard.AsConflict(() =>
                    storedToken.Rotate(occurredAt, newRefreshToken.Entity.TokenHash));
                await _authSessionRepository.AddRefreshTokenAsync(
                    newRefreshToken.Entity,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = _tokenIssuer.BuildResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    newRefreshToken.RawToken,
                    newRefreshToken.Entity.ExpiresAt,
                    newRefreshToken.Entity.FamilyId,
                    occurredAt);
                telemetry.Complete();
                return response;
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw Unauthorized();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task LogoutAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.logout", cancellationToken);
            var tokenHash = AuthTokenIssuer.HashRefreshToken(
                request.RefreshToken);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId,
                    activeOnly: false,
                    cancellationToken);
                if (user != null)
                {
                    var storedToken = await LoadRefreshTokenForUpdateAsync(
                        tokenHash,
                        cancellationToken);
                    if (storedToken != null && storedToken.UserId == user.Id)
                    {
                        await RevokeTokenFamilyAsync(
                            user.Id,
                            storedToken.FamilyId,
                            "Logout",
                            UtcNow,
                            cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                }

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.logout_all", cancellationToken);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");
                var occurredAt = UtcNow;
                await RevokeAllUserTokensAsync(
                    user.Id,
                    "Logout all",
                    occurredAt,
                    cancellationToken);
                DomainRuleGuard.AsConflict(user.InvalidateSessions);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task<Guid?> FindRefreshTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken)
            => await _authSessionRepository.GetRefreshTokenOwnerIdAsync(
                tokenHash,
                cancellationToken);

        private async Task<RefreshToken?> LoadRefreshTokenForUpdateAsync(
            string tokenHash,
            CancellationToken cancellationToken)
            => await _consistency.LockRefreshTokenAsync(tokenHash, cancellationToken);

        private async Task RevokeTokenFamilyAsync(
            Guid userId,
            Guid familyId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens =
                await _authSessionRepository.GetActiveRefreshTokenFamilyAsync(
                    userId,
                    familyId,
                    cancellationToken);
            RevokeTokens(tokens, reason, occurredAt);
        }

        private async Task RevokeAllUserTokensAsync(
            Guid userId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens =
                await _authSessionRepository.GetActiveRefreshTokensAsync(
                    userId,
                    cancellationToken);
            RevokeTokens(tokens, reason, occurredAt);
        }

        private static void RevokeTokens(
            IEnumerable<RefreshToken> tokens,
            string reason,
            DateTime occurredAt)
        {
            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt, reason));
            }
        }

        private async Task LoadRolesAndPermissionsAsync(
            User user,
            CancellationToken cancellationToken)
            => await _userRepository.LoadRolesAsync(
                user,
                includePermissions: true,
                cancellationToken);
        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

        private static IEnumerable<string> GetRoles(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .Select(userRole => userRole.Role!.Name);

        private static IEnumerable<string> GetPermissions(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .SelectMany(userRole => userRole.Role!.RolePermissions)
                .Where(rolePermission => rolePermission.Permission != null)
                .Select(rolePermission => rolePermission.Permission!.Name);

        private static ApiException Unauthorized()
            => new(401, "unauthorized", "Tên đăng nhập, mật khẩu hoặc token không hợp lệ.");

    }
}
