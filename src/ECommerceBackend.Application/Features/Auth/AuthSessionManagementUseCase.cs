using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthSessionManagementUseCase
    {
        private readonly IAuthSessionRepository _sessions;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;

        public AuthSessionManagementUseCase(
            IAuthSessionRepository sessions,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _sessions = sessions;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
        }

        public async Task<IReadOnlyList<AuthSessionResponse>> GetAsync(
            Guid userId,
            Guid currentSessionId,
            CancellationToken cancellationToken = default)
        {
            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            var sessions = await _sessions.GetActiveSessionsAsync(
                userId,
                occurredAt,
                cancellationToken);
            return sessions
                .Select(session => new AuthSessionResponse
                {
                    SessionId = session.SessionId,
                    LastRefreshedAt = session.LastRefreshedAt,
                    ExpiresAt = session.ExpiresAt,
                    IsCurrent = session.SessionId == currentSessionId
                })
                .ToArray();
        }

        public async Task RevokeAsync(
            Guid userId,
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            if (sessionId == Guid.Empty)
                throw new NotFoundException("Không tìm thấy phiên đăng nhập.");

            using var telemetry = BusinessTelemetry.Start(
                "auth.session.revoke",
                cancellationToken);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                _ = await _consistency.LockUserAsync(
                    userId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");
                var tokens = await _sessions.GetActiveRefreshTokenFamilyAsync(
                    userId,
                    sessionId,
                    cancellationToken);
                if (tokens.Count == 0)
                    throw new NotFoundException("Không tìm thấy phiên đăng nhập đang hoạt động.");

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                AuthSessionRules.RevokeTokens(tokens, "Session revoked", occurredAt);
                _audit.Write(
                    "auth.session.revoked",
                    "AuthSession",
                    sessionId.ToString(),
                    userId);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (Exception ex) when (
                _consistency.IsConcurrencyConflict(ex)
                || _consistency.IsDeadlock(ex))
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
