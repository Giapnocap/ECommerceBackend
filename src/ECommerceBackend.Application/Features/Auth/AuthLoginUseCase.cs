using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthLoginUseCase
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

        public AuthLoginUseCase(
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

        public async Task<AuthResponse> ExecuteAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.login",
                cancellationToken);
            var normalizedUserName = AuthSessionRules.Normalize(
                request.UserName);
            var userId =
                await _userRepository.GetActiveUserIdByUserNameAsync(
                    normalizedUserName,
                    cancellationToken);
            if (!userId.HasValue)
            {
                _ = _passwordHasher.Verify(request.Password, null);
                throw AuthSessionRules.Unauthorized();
            }

            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId.Value,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw AuthSessionRules.Unauthorized();

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var passwordValid = _passwordHasher.Verify(
                    request.Password,
                    user.PasswordHash);
                if (user.IsLockedOutAt(occurredAt))
                {
                    telemetry.SetTag("auth.account.locked", true);
                    throw AuthSessionRules.Unauthorized();
                }

                if (!passwordValid)
                {
                    var locked = DomainRuleGuard.AsConflict(() =>
                        user.RecordFailedLogin(
                            occurredAt,
                            _securityOptions.MaxFailedLoginAttempts,
                            TimeSpan.FromMinutes(
                                _securityOptions.LockoutMinutes)));
                    if (locked)
                    {
                        telemetry.SetTag("auth.account.locked", true);
                        _audit.Write(
                            "auth.account.locked",
                            nameof(User),
                            user.Id.ToString(),
                            metadata: new Dictionary<string, object?>
                            {
                                ["lockoutMinutes"] =
                                    _securityOptions.LockoutMinutes,
                                ["failedAttempts"] = user.FailedLoginCount
                            });
                    }

                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    throw AuthSessionRules.Unauthorized();
                }

                user.ClearLoginFailures();
                await _userRepository.LoadRolesAsync(
                    user,
                    includePermissions: true,
                    cancellationToken);
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
                    AuthSessionRules.GetRoles(user),
                    AuthSessionRules.GetPermissions(user),
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
    }
}
