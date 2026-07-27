using System.Data;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class PasswordResetUseCase
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly AuthSecurityOptions _options;
        private readonly TimeProvider _timeProvider;

        public PasswordResetUseCase(
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IPasswordHasher passwordHasher,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IOptions<AuthSecurityOptions> options,
            TimeProvider timeProvider)
        {
            _userRepository = userRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _passwordHasher = passwordHasher;
            _outbox = outbox;
            _audit = audit;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        public async Task RequestAsync(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.password_reset.request",
                cancellationToken);
            var normalizedEmail = request.Email.Trim().ToUpperInvariant();
            var userId =
                await _userRepository.GetActiveUserIdByEmailAsync(
                    normalizedEmail,
                    cancellationToken);
            if (!userId.HasValue)
            {
                telemetry.Complete();
                return;
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
                    cancellationToken);
                if (user == null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    telemetry.Complete();
                    return;
                }

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                await RevokePasswordResetTokensAsync(
                    user.Id,
                    exceptTokenId: null,
                    occurredAt,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                var rawToken = Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(48));
                _authSessionRepository.AddPasswordResetToken(
                    new PasswordResetToken
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TokenHash = HashToken(rawToken),
                    CreatedAt = occurredAt,
                    ExpiresAt = occurredAt.AddMinutes(
                        _options.PasswordResetTokenMinutes)
                });
                _outbox.EnqueueSensitiveNotification(
                    user.Id,
                    "Đặt lại mật khẩu",
                    BuildMessage(rawToken));
                _audit.Write(
                    "auth.password_reset.requested",
                    nameof(User),
                    user.Id.ToString());

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task ResetAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.password_reset.complete",
                cancellationToken);
            var tokenHash = HashToken(request.Token.Trim());
            var userId =
                await _authSessionRepository.GetPasswordResetTokenOwnerIdAsync(
                    tokenHash,
                    cancellationToken)
                ?? throw InvalidToken();
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
                    ?? throw InvalidToken();
                var token =
                    await _authSessionRepository.GetPasswordResetTokenAsync(
                        tokenHash,
                        cancellationToken)
                    ?? throw InvalidToken();
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                if (!token.IsActiveAt(occurredAt))
                    throw InvalidToken();
                if (_passwordHasher.Verify(
                    request.NewPassword,
                    user.PasswordHash))
                {
                    throw new ConflictException(
                        "password_reuse",
                        "Mật khẩu mới phải khác mật khẩu hiện tại.");
                }

                DomainRuleGuard.AsConflict(() => token.Consume(occurredAt));
                DomainRuleGuard.AsConflict(() => user.ChangePasswordHash(
                    _passwordHasher.Hash(request.NewPassword),
                    occurredAt));
                await RevokeAllUserTokensAsync(
                    user.Id,
                    "Password reset",
                    occurredAt,
                    cancellationToken);
                await RevokePasswordResetTokensAsync(
                    user.Id,
                    token.Id,
                    occurredAt,
                    cancellationToken);
                _audit.Write(
                    "auth.password_reset.completed",
                    nameof(User),
                    user.Id.ToString());

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw InvalidToken();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException(
                    "password_reset_concurrency_conflict",
                    "Mật khẩu hoặc mã đặt lại đang được xử lý "
                    + "bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
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
            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() =>
                    token.Revoke(occurredAt, reason));
            }
        }

        private async Task RevokePasswordResetTokensAsync(
            Guid userId,
            Guid? exceptTokenId,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens =
                await _authSessionRepository.GetActivePasswordResetTokensAsync(
                    userId,
                    exceptTokenId,
                    cancellationToken);
            foreach (var token in tokens)
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt));
        }

        private static string HashToken(string token)
            => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private string BuildMessage(string rawToken)
        {
            var url = new UriBuilder(_options.PasswordResetUrl)
            {
                Query = $"token={Uri.EscapeDataString(rawToken)}"
            }.Uri.AbsoluteUri;
            return $"Mở liên kết sau để đặt lại mật khẩu: {url}\n"
                + $"Liên kết có hiệu lực trong "
                + $"{_options.PasswordResetTokenMinutes} phút.";
        }

        private static ApiException InvalidToken()
            => new(
                400,
                "invalid_password_reset_token",
                "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");
    }
}
