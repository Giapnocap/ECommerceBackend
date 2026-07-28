using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class AuthSessionRepository : IAuthSessionRepository
    {
        private readonly AppDbContext _context;

        public AuthSessionRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<Guid?> GetRefreshTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
            => _context.RefreshTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (Guid?)token.UserId)
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<IReadOnlyList<RefreshToken>> GetActiveRefreshTokensAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => await _context.RefreshTokens
                .Where(token => token.UserId == userId
                    && token.RevokedAt == null)
                .ToListAsync(cancellationToken);

        public async Task<IReadOnlyList<RefreshToken>> GetActiveRefreshTokenFamilyAsync(
            Guid userId,
            Guid familyId,
            CancellationToken cancellationToken = default)
            => await _context.RefreshTokens
                .Where(token => token.UserId == userId
                    && token.FamilyId == familyId
                    && token.RevokedAt == null)
                .ToListAsync(cancellationToken);

        public Task AddRefreshTokenAsync(
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
            => _context.RefreshTokens
                .AddAsync(refreshToken, cancellationToken)
                .AsTask();

        public Task<Guid?> GetPasswordResetTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
            => _context.PasswordResetTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (Guid?)token.UserId)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<PasswordResetToken?> GetPasswordResetTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
            => _context.PasswordResetTokens.SingleOrDefaultAsync(
                candidate => candidate.TokenHash == tokenHash,
                cancellationToken);

        public async Task<IReadOnlyList<PasswordResetToken>> GetActivePasswordResetTokensAsync(
            Guid userId,
            Guid? excludedTokenId,
            CancellationToken cancellationToken = default)
            => await _context.PasswordResetTokens
                .Where(token => token.UserId == userId
                    && token.ConsumedAt == null
                    && token.RevokedAt == null
                    && (!excludedTokenId.HasValue
                        || token.Id != excludedTokenId.Value))
                .ToListAsync(cancellationToken);

        public void AddPasswordResetToken(PasswordResetToken token)
            => _context.PasswordResetTokens.Add(token);
    }
}
