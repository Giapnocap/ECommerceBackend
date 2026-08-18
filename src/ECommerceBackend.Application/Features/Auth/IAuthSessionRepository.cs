using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public sealed record AuthSessionRecord(
        Guid SessionId,
        DateTime LastRefreshedAt,
        DateTime ExpiresAt);

    public interface IAuthSessionRepository
    {
        Task<Guid?> GetRefreshTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<RefreshToken>> GetActiveRefreshTokensAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<RefreshToken>> GetActiveRefreshTokenFamilyAsync(
            Guid userId,
            Guid familyId,
            CancellationToken cancellationToken = default);

        Task AddRefreshTokenAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AuthSessionRecord>> GetActiveSessionsAsync(
            Guid userId,
            DateTime occurredAt,
            CancellationToken cancellationToken = default);

        Task<Guid?> GetPasswordResetTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken = default);

        Task<PasswordResetToken?> GetPasswordResetTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PasswordResetToken>> GetActivePasswordResetTokensAsync(
            Guid userId,
            Guid? excludedTokenId,
            CancellationToken cancellationToken = default);

        void AddPasswordResetToken(PasswordResetToken token);

        Task<Guid?> GetEmailVerificationTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken = default);

        Task<EmailVerificationToken?> GetEmailVerificationTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<EmailVerificationToken>> GetActiveEmailVerificationTokensAsync(
            Guid userId,
            Guid? excludedTokenId,
            CancellationToken cancellationToken = default);

        void AddEmailVerificationToken(EmailVerificationToken token);
    }
}
