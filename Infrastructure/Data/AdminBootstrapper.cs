using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Data
{
    public sealed class AdminBootstrapper
    {
        public const string LegacyPasswordMarker = "!BOOTSTRAP_REQUIRED!";
        private static readonly Guid LegacyAdminId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        private readonly AppDbContext _context;
        private readonly AdminBootstrapOptions _options;
        private readonly ILogger<AdminBootstrapper> _logger;
        private readonly TimeProvider _timeProvider;

        public AdminBootstrapper(
            AppDbContext context,
            IOptions<AdminBootstrapOptions> options,
            ILogger<AdminBootstrapper> logger)
            : this(context, options, logger, TimeProvider.System)
        {
        }

        public AdminBootstrapper(
            AppDbContext context,
            IOptions<AdminBootstrapOptions> options,
            ILogger<AdminBootstrapper> logger,
            TimeProvider timeProvider)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                return;

            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            await using var transaction = _context.Database.IsRelational()
                ? await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
                : null;

            try
            {
                var adminRole = await _context.Roles
                    .SingleOrDefaultAsync(role => role.Name == RoleNames.Admin, cancellationToken)
                    ?? throw new InvalidOperationException("Admin role is missing. Apply database migrations first.");

                var hasUsableAdmin = await _context.UserRoles
                    .AnyAsync(userRole => userRole.RoleId == adminRole.Id
                        && userRole.User != null
                        && !userRole.User.IsDeleted
                        && userRole.User.PasswordHash != LegacyPasswordMarker, cancellationToken);

                if (hasUsableAdmin)
                {
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);

                    return;
                }

                var normalizedUserName = Normalize(_options.UserName);
                var normalizedEmail = Normalize(_options.Email);
                var user = await _context.Users
                    .Include(candidate => candidate.UserRoles)
                    .SingleOrDefaultAsync(candidate => candidate.NormalizedUserName == normalizedUserName
                        || candidate.NormalizedEmail == normalizedEmail, cancellationToken);

                if (user != null && user.Id != LegacyAdminId && user.PasswordHash != LegacyPasswordMarker)
                {
                    throw new ConflictException(
                        "Admin bootstrap identity already belongs to a non-bootstrap account. Choose another username and email.");
                }

                if (user == null)
                {
                    user = new User
                    {
                        Id = Guid.NewGuid(),
                        UserName = _options.UserName.Trim(),
                        NormalizedUserName = normalizedUserName,
                        Email = _options.Email.Trim(),
                        NormalizedEmail = normalizedEmail,
                        FullName = _options.FullName.Trim(),
                        Phone = NormalizeOptional(_options.Phone),
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(_options.Password),
                        CreatedAt = occurredAt
                    };

                    _context.Users.Add(user);
                    _context.Carts.Add(new Cart { Id = Guid.NewGuid(), UserId = user.Id });
                }
                else
                {
                    if (user.IsDeleted)
                        throw new InvalidOperationException("The configured bootstrap account is deleted and cannot be reactivated automatically.");

                    user.UserName = _options.UserName.Trim();
                    user.NormalizedUserName = normalizedUserName;
                    user.Email = _options.Email.Trim();
                    user.NormalizedEmail = normalizedEmail;
                    user.FullName = _options.FullName.Trim();
                    user.Phone = NormalizeOptional(_options.Phone);
                    user.ChangePasswordHash(
                        BCrypt.Net.BCrypt.HashPassword(_options.Password),
                        occurredAt);

                    if (!await _context.Carts.AnyAsync(cart => cart.UserId == user.Id, cancellationToken))
                        _context.Carts.Add(new Cart { Id = Guid.NewGuid(), UserId = user.Id });
                }

                if (user.UserRoles.All(userRole => userRole.RoleId != adminRole.Id))
                {
                    _context.UserRoles.Add(new UserRole
                    {
                        UserId = user.Id,
                        RoleId = adminRole.Id
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                if (transaction != null)
                    await transaction.CommitAsync(cancellationToken);

                _logger.LogWarning(
                    "Created or recovered bootstrap admin {UserName}. Disable AdminBootstrap after the first successful startup.",
                    user.UserName);
            }
            catch
            {
                if (transaction != null)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
