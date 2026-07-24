using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuthRegistrationUseCase
    {
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IPasswordHasher _passwordHasher;
        private readonly AuthTokenIssuer _tokenIssuer;
        private readonly TimeProvider _timeProvider;

        public AuthRegistrationUseCase(
            IAppDbContext context,
            IDataConsistencyService consistency,
            IPasswordHasher passwordHasher,
            AuthTokenIssuer tokenIssuer,
            TimeProvider timeProvider)
        {
            _context = context;
            _consistency = consistency;
            _passwordHasher = passwordHasher;
            _tokenIssuer = tokenIssuer;
            _timeProvider = timeProvider;
        }

        public async Task<AuthResponse> ExecuteAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.register",
                cancellationToken);
            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            var userName = request.UserName.Trim();
            var email = request.Email.Trim();
            var normalizedUserName = Normalize(userName);
            var normalizedEmail = Normalize(email);

            if (await _context.Users.AnyAsync(
                user => user.NormalizedUserName == normalizedUserName,
                cancellationToken))
            {
                throw new ConflictException(
                    "username_conflict",
                    $"Tên đăng nhập '{userName}' đã tồn tại.");
            }

            if (await _context.Users.AnyAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken))
            {
                throw new ConflictException(
                    "email_conflict",
                    $"Email '{email}' đã được sử dụng.");
            }

            var customerRole = await _context.Roles
                .Include(role => role.RolePermissions)
                    .ThenInclude(rolePermission => rolePermission.Permission)
                .FirstOrDefaultAsync(
                    role => role.Name == RoleNames.Customer,
                    cancellationToken)
                ?? throw new NotFoundException(
                    "Không tìm thấy vai trò khách hàng. "
                    + "Hãy áp dụng bản cập nhật cơ sở dữ liệu.");
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                FullName = request.FullName.Trim(),
                Phone = NormalizeOptional(request.Phone),
                PasswordHash = _passwordHasher.Hash(request.Password),
                CreatedAt = occurredAt
            };
            await _context.Users.AddAsync(user, cancellationToken);
            await _context.UserRoles.AddAsync(
                new UserRole { UserId = user.Id, RoleId = customerRole.Id },
                cancellationToken);
            await _context.Carts.AddAsync(
                new Cart { Id = Guid.NewGuid(), UserId = user.Id },
                cancellationToken);
            var refreshToken = _tokenIssuer.CreateRefreshToken(
                user.Id,
                Guid.NewGuid(),
                occurredAt);
            await _context.RefreshTokens.AddAsync(
                refreshToken.Entity,
                cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
                when (_consistency.IsUniqueConstraintViolation(ex))
            {
                throw new ConflictException(
                    "identity_conflict",
                    "Tên đăng nhập hoặc email đã được sử dụng "
                    + "bởi một yêu cầu khác.",
                    ex);
            }

            var response = _tokenIssuer.BuildResponse(
                user,
                [customerRole.Name],
                customerRole.RolePermissions
                    .Where(item => item.Permission != null)
                    .Select(item => item.Permission!.Name),
                refreshToken.RawToken,
                refreshToken.Entity.ExpiresAt,
                refreshToken.Entity.FamilyId,
                occurredAt);
            telemetry.Complete();
            return response;
        }

        private static string Normalize(string value)
            => value.Trim().ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
