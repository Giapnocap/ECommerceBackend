using System.Data;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthLogoutUseCase
    {
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly TimeProvider _timeProvider;

        public AuthLogoutUseCase(
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            TimeProvider timeProvider)
        {
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _timeProvider = timeProvider;
        }

        public async Task ExecuteAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.logout",
                cancellationToken);
            var tokenHash = AuthTokenIssuer.HashRefreshToken(
                request.RefreshToken);
            await using var transaction =
                await _consistency.BeginTransactionAsync(
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
                    var storedToken =
                        await _consistency.LockRefreshTokenAsync(
                            tokenHash,
                            cancellationToken);
                    if (storedToken != null
                        && storedToken.UserId == user.Id)
                    {
                        var tokens = await _authSessionRepository
                            .GetActiveRefreshTokenFamilyAsync(
                                user.Id,
                                storedToken.FamilyId,
                                cancellationToken);
                        AuthSessionRules.RevokeTokens(
                            tokens,
                            "Logout",
                            _timeProvider.GetUtcNow().UtcDateTime);
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
