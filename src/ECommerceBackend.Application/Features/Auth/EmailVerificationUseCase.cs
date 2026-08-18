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
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class EmailVerificationUseCase
    {
        private readonly IAuthSessionRepository _tokens;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly AuthSecurityOptions _options;
        private readonly TimeProvider _timeProvider;

        public EmailVerificationUseCase(
            IAuthSessionRepository tokens,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IOutboxWriter outbox,
            IAuditWriter audit,
            IOptions<AuthSecurityOptions> options,
            TimeProvider timeProvider)
        {
            _tokens = tokens;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _outbox = outbox;
            _audit = audit;
            _options = options.Value;
            _timeProvider = timeProvider;
        }

        internal void IssueForRegistration(User user, DateTime occurredAt)
            => Issue(user, occurredAt);

        public async Task RequestAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.email_verification.request",
                cancellationToken);
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
                if (user.EmailVerifiedAt.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    telemetry.Complete();
                    return;
                }

                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                await RevokeActiveTokensAsync(
                    user.Id,
                    excludedTokenId: null,
                    occurredAt,
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                Issue(user, occurredAt);
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

        public async Task ConfirmAsync(
            ConfirmEmailRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.email_verification.confirm",
                cancellationToken);
            var tokenHash = HashToken(request.Token.Trim());
            var userId = await _tokens.GetEmailVerificationTokenOwnerIdAsync(
                tokenHash,
                cancellationToken)
                ?? throw InvalidToken();
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
                    ?? throw InvalidToken();
                var token = await _tokens.GetEmailVerificationTokenAsync(
                    tokenHash,
                    cancellationToken)
                    ?? throw InvalidToken();
                var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
                if (!token.IsActiveAt(occurredAt))
                    throw InvalidToken();

                DomainRuleGuard.AsConflict(() => token.Consume(occurredAt));
                DomainRuleGuard.AsConflict(() => user.VerifyEmail(occurredAt));
                await RevokeActiveTokensAsync(
                    user.Id,
                    token.Id,
                    occurredAt,
                    cancellationToken);
                _audit.Write(
                    "auth.email_verified",
                    nameof(User),
                    user.Id.ToString(),
                    user.Id);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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
                    "email_verification_concurrency_conflict",
                    "Mã xác minh email đang được xử lý. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private void Issue(User user, DateTime occurredAt)
        {
            var rawToken = Convert.ToBase64String(
                RandomNumberGenerator.GetBytes(48));
            var token = DomainRuleGuard.AsBusiness(() =>
                EmailVerificationToken.Create(
                    Guid.NewGuid(),
                    user.Id,
                    HashToken(rawToken),
                    occurredAt,
                    occurredAt.AddMinutes(
                        _options.EmailVerificationTokenMinutes)));
            _tokens.AddEmailVerificationToken(token);
            _outbox.EnqueueSensitiveNotification(
                user.Id,
                "Xác minh địa chỉ email",
                BuildMessage(rawToken));
            _audit.Write(
                "auth.email_verification.requested",
                nameof(User),
                user.Id.ToString(),
                user.Id);
        }

        private async Task RevokeActiveTokensAsync(
            Guid userId,
            Guid? excludedTokenId,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var activeTokens = await _tokens.GetActiveEmailVerificationTokensAsync(
                userId,
                excludedTokenId,
                cancellationToken);
            foreach (var token in activeTokens)
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt));
        }

        private static string HashToken(string token)
            => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private string BuildMessage(string rawToken)
        {
            var url = new UriBuilder(_options.EmailVerificationUrl)
            {
                Query = $"token={Uri.EscapeDataString(rawToken)}"
            }.Uri.AbsoluteUri;
            return $"Mở liên kết sau để xác minh email: {url}\n"
                + $"Liên kết có hiệu lực trong "
                + $"{_options.EmailVerificationTokenMinutes} phút.";
        }

        private static ApiException InvalidToken()
            => new(
                400,
                "invalid_email_verification_token",
                "Mã xác minh email không hợp lệ hoặc đã hết hạn.");
    }
}
