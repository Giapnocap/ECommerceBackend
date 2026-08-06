using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthLogoutAllUseCase
    {
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly TimeProvider _timeProvider;

        public AuthLogoutAllUseCase(
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
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.logout_all",
                cancellationToken);
            await using var transaction =
                await _consistency.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw new NotFoundException(
                        "Không tìm thấy người dùng.");
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                var tokens =
                    await _authSessionRepository.GetActiveRefreshTokensAsync(
                        user.Id,
                        cancellationToken);
                AuthSessionRules.RevokeTokens(
                    tokens,
                    "Logout all",
                    occurredAt);
                DomainRuleGuard.AsConflict(user.InvalidateSessions);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (Exception ex)
                when (_consistency.IsConcurrencyConflict(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw AuthSessionRules.SessionConflict(ex);
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
