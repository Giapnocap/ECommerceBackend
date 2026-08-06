using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthRefreshUseCase
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly AuthTokenIssuer _tokenIssuer;
        private readonly TimeProvider _timeProvider;

        public AuthRefreshUseCase(
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            AuthTokenIssuer tokenIssuer,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _tokenIssuer = tokenIssuer;
            _timeProvider = timeProvider;
        }

        public async Task<AuthResponse> ExecuteAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.refresh",
                cancellationToken);
            var tokenHash = AuthTokenIssuer.HashRefreshToken(
                request.RefreshToken);
            var tokenOwnerId =
                await _authSessionRepository.GetRefreshTokenOwnerIdAsync(
                    tokenHash,
                    cancellationToken)
                ?? throw AuthSessionRules.Unauthorized();
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    tokenOwnerId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw AuthSessionRules.Unauthorized();
                var storedToken =
                    await _consistency.LockRefreshTokenAsync(
                        tokenHash,
                        cancellationToken)
                    ?? throw AuthSessionRules.Unauthorized();
                if (storedToken.UserId != user.Id)
                    throw AuthSessionRules.Unauthorized();

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                if (storedToken.RevokedAt.HasValue)
                {
                    if (!string.IsNullOrWhiteSpace(
                        storedToken.ReplacedByTokenHash))
                    {
                        var tokens = await _authSessionRepository
                            .GetActiveRefreshTokenFamilyAsync(
                                storedToken.UserId,
                                storedToken.FamilyId,
                                cancellationToken);
                        AuthSessionRules.RevokeTokens(
                            tokens,
                            "Refresh token reuse detected",
                            occurredAt);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        transactionCompleted = true;
                    }

                    throw AuthSessionRules.Unauthorized();
                }

                if (storedToken.IsExpiredAt(occurredAt))
                    throw AuthSessionRules.Unauthorized();

                await _userRepository.LoadRolesAsync(
                    user,
                    includePermissions: true,
                    cancellationToken);
                var newRefreshToken = _tokenIssuer.CreateRefreshToken(
                    user.Id,
                    storedToken.FamilyId,
                    occurredAt);
                DomainRuleGuard.AsConflict(() =>
                    storedToken.Rotate(
                        occurredAt,
                        newRefreshToken.Entity.TokenHash));
                await _authSessionRepository.AddRefreshTokenAsync(
                    newRefreshToken.Entity,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = _tokenIssuer.BuildResponse(
                    user,
                    AuthSessionRules.GetRoles(user),
                    AuthSessionRules.GetPermissions(user),
                    newRefreshToken.RawToken,
                    newRefreshToken.Entity.ExpiresAt,
                    newRefreshToken.Entity.FamilyId,
                    occurredAt);
                telemetry.Complete();
                return response;
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw AuthSessionRules.Unauthorized();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw AuthSessionRules.SessionConflict(ex);
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
