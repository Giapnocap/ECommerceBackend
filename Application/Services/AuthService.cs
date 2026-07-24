using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IGenericRepository<User> _userRepo;
        private readonly IGenericRepository<Role> _roleRepo;
        private readonly IGenericRepository<UserRole> _userRoleRepo;
        private readonly IGenericRepository<Cart> _cartRepo;
        private readonly IGenericRepository<RefreshToken> _refreshTokenRepo;
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly JwtOptions _jwtOptions;
        private readonly TimeProvider _timeProvider;

        public AuthService(
            IGenericRepository<User> userRepo,
            IGenericRepository<Role> roleRepo,
            IGenericRepository<UserRole> userRoleRepo,
            IGenericRepository<Cart> cartRepo,
            IGenericRepository<RefreshToken> refreshTokenRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IOptions<JwtOptions> jwtOptions)
            : this(
                userRepo,
                roleRepo,
                userRoleRepo,
                cartRepo,
                refreshTokenRepo,
                context,
                consistency,
                jwtOptions,
                TimeProvider.System)
        {
        }

        public AuthService(
            IGenericRepository<User> userRepo,
            IGenericRepository<Role> roleRepo,
            IGenericRepository<UserRole> userRoleRepo,
            IGenericRepository<Cart> cartRepo,
            IGenericRepository<RefreshToken> refreshTokenRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IOptions<JwtOptions> jwtOptions,
            TimeProvider timeProvider)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _userRoleRepo = userRoleRepo;
            _cartRepo = cartRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _context = context;
            _consistency = consistency;
            _jwtOptions = jwtOptions.Value;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            var occurredAt = UtcNow;
            var userName = request.UserName.Trim();
            var email = request.Email.Trim();
            var normalizedUserName = Normalize(userName);
            var normalizedEmail = Normalize(email);

            if (await _userRepo.Query().AnyAsync(user => user.NormalizedUserName == normalizedUserName))
                throw new ConflictException("username_conflict", $"Tên đăng nhập '{userName}' đã tồn tại.");

            if (await _userRepo.Query().AnyAsync(user => user.NormalizedEmail == normalizedEmail))
                throw new ConflictException("email_conflict", $"Email '{email}' đã được sử dụng.");

            var customerRole = await _roleRepo.Query()
                .Include(role => role.RolePermissions)
                    .ThenInclude(rolePermission => rolePermission.Permission)
                .FirstOrDefaultAsync(role => role.Name == RoleNames.Customer)
                ?? throw new NotFoundException("Không tìm thấy vai trò khách hàng. Hãy áp dụng bản cập nhật cơ sở dữ liệu.");

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                NormalizedUserName = normalizedUserName,
                Email = email,
                NormalizedEmail = normalizedEmail,
                FullName = request.FullName.Trim(),
                Phone = NormalizeOptional(request.Phone),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                CreatedAt = occurredAt
            };

            await _userRepo.AddAsync(user);
            await _userRoleRepo.AddAsync(new UserRole { UserId = user.Id, RoleId = customerRole.Id });
            await _cartRepo.AddAsync(new Cart { Id = Guid.NewGuid(), UserId = user.Id });

            var refreshToken = CreateRefreshToken(user.Id, Guid.NewGuid(), occurredAt);
            await _refreshTokenRepo.AddAsync(refreshToken.Entity);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                throw new ConflictException(
                    "identity_conflict",
                    "Tên đăng nhập hoặc email đã được sử dụng bởi một yêu cầu khác.",
                    ex);
            }

            return BuildAuthResponse(
                user,
                [customerRole.Name],
                customerRole.RolePermissions
                    .Where(rolePermission => rolePermission.Permission != null)
                    .Select(rolePermission => rolePermission.Permission!.Name),
                refreshToken.RawToken,
                refreshToken.Entity.ExpiresAt,
                refreshToken.Entity.FamilyId,
                occurredAt);
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var normalizedUserName = Normalize(request.UserName);
            var userId = await _userRepo.Query()
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.NormalizedUserName == normalizedUserName)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync()
                ?? throw Unauthorized();

            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(userId, activeOnly: true)
                    ?? throw Unauthorized();

                if (!IsBcryptHash(user.PasswordHash)
                    || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    throw Unauthorized();
                }

                await LoadRolesAndPermissionsAsync(user);
                var occurredAt = UtcNow;
                var refreshToken = CreateRefreshToken(user.Id, Guid.NewGuid(), occurredAt);
                await _refreshTokenRepo.AddAsync(refreshToken.Entity);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                transactionCompleted = true;

                return BuildAuthResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    refreshToken.RawToken,
                    refreshToken.Entity.ExpiresAt,
                    refreshToken.Entity.FamilyId,
                    occurredAt);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request)
        {
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var tokenOwnerId = await FindRefreshTokenOwnerIdAsync(tokenHash)
                ?? throw Unauthorized();
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(tokenOwnerId, activeOnly: true)
                    ?? throw Unauthorized();
                var storedToken = await LoadRefreshTokenForUpdateAsync(tokenHash)
                    ?? throw Unauthorized();
                if (storedToken.UserId != user.Id)
                    throw Unauthorized();

                var occurredAt = UtcNow;
                if (storedToken.RevokedAt.HasValue)
                {
                    if (!string.IsNullOrWhiteSpace(storedToken.ReplacedByTokenHash))
                    {
                        await RevokeTokenFamilyAsync(
                            storedToken.UserId,
                            storedToken.FamilyId,
                            "Refresh token reuse detected",
                            occurredAt);
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        transactionCompleted = true;
                    }

                    throw Unauthorized();
                }

                if (storedToken.IsExpiredAt(occurredAt))
                    throw Unauthorized();

                await LoadRolesAndPermissionsAsync(user);
                var newRefreshToken = CreateRefreshToken(user.Id, storedToken.FamilyId, occurredAt);
                DomainRuleGuard.AsConflict(() =>
                    storedToken.Rotate(occurredAt, newRefreshToken.Entity.TokenHash));
                await _refreshTokenRepo.AddAsync(newRefreshToken.Entity);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                transactionCompleted = true;

                return BuildAuthResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    newRefreshToken.RawToken,
                    newRefreshToken.Entity.ExpiresAt,
                    newRefreshToken.Entity.FamilyId,
                    occurredAt);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw Unauthorized();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task LogoutAsync(Guid userId, LogoutRequest request)
        {
            var tokenHash = HashRefreshToken(request.RefreshToken);
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(userId, activeOnly: false);
                if (user != null)
                {
                    var storedToken = await LoadRefreshTokenForUpdateAsync(tokenHash);
                    if (storedToken != null && storedToken.UserId == user.Id)
                    {
                        await RevokeTokenFamilyAsync(
                            user.Id,
                            storedToken.FamilyId,
                            "Logout",
                            UtcNow);
                        await _context.SaveChangesAsync();
                    }
                }

                await transaction.CommitAsync();
                transactionCompleted = true;
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task LogoutAllAsync(Guid userId)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(userId, activeOnly: true)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");
                var occurredAt = UtcNow;
                await RevokeAllUserTokensAsync(user.Id, "Logout all", occurredAt);
                DomainRuleGuard.AsConflict(user.InvalidateSessions);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                transactionCompleted = true;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Phiên đăng nhập vừa được thay đổi bởi một yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task<Guid?> FindRefreshTokenOwnerIdAsync(string tokenHash)
            => await _context.RefreshTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (Guid?)token.UserId)
                .SingleOrDefaultAsync();

        private async Task<RefreshToken?> LoadRefreshTokenForUpdateAsync(string tokenHash)
            => await _consistency.LockRefreshTokenAsync(tokenHash);

        private async Task RevokeTokenFamilyAsync(
            Guid userId,
            Guid familyId,
            string reason,
            DateTime occurredAt)
        {
            var tokens = await _context.RefreshTokens
                .Where(token => token.UserId == userId
                    && token.FamilyId == familyId
                    && token.RevokedAt == null)
                .ToListAsync();
            RevokeTokens(tokens, reason, occurredAt);
        }

        private async Task RevokeAllUserTokensAsync(
            Guid userId,
            string reason,
            DateTime occurredAt)
        {
            var tokens = await _context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync();
            RevokeTokens(tokens, reason, occurredAt);
        }

        private static void RevokeTokens(
            IEnumerable<RefreshToken> tokens,
            string reason,
            DateTime occurredAt)
        {
            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt, reason));
            }
        }

        private async Task LoadRolesAndPermissionsAsync(User user)
            => await _context.Entry(user)
                .Collection(candidate => candidate.UserRoles)
                .Query()
                .Include(userRole => userRole.Role)
                    .ThenInclude(role => role!.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
                .LoadAsync();
        private AuthResponse BuildAuthResponse(
            User user,
            IEnumerable<string> roles,
            IEnumerable<string> permissions,
            string refreshToken,
            DateTime refreshTokenExpiresAt,
            Guid sessionId,
            DateTime issuedAt)
        {
            var roleList = roles.Distinct(StringComparer.Ordinal).ToArray();
            var permissionList = permissions.Distinct(StringComparer.Ordinal).ToArray();
            var accessToken = GenerateJwt(user, roleList, permissionList, sessionId, issuedAt, out var accessTokenExpiresAt);

            return new AuthResponse
            {
                UserId = user.Id,
                Token = accessToken,
                AccessToken = accessToken,
                AccessTokenExpiresAt = accessTokenExpiresAt,
                RefreshToken = refreshToken,
                RefreshTokenExpiresAt = refreshTokenExpiresAt,
                UserName = user.UserName,
                FullName = user.FullName,
                Email = user.Email,
                Roles = roleList,
                Permissions = permissionList
            };
        }

        private string GenerateJwt(
            User user,
            IEnumerable<string> roles,
            IEnumerable<string> permissions,
            Guid sessionId,
            DateTime issuedAt,
            out DateTime expiresAt)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            expiresAt = issuedAt.AddMinutes(_jwtOptions.AccessTokenMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.UserName),
                new(ClaimTypes.Email, user.Email),
                new(AuthClaimTypes.TokenVersion, user.TokenVersion.ToString()),
                new(AuthClaimTypes.SessionId, sessionId.ToString())
            };

            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            claims.AddRange(permissions.Select(permission => new Claim(AuthClaimTypes.Permission, permission)));

            var token = new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                expires: expiresAt,
                claims: claims,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private RefreshTokenPair CreateRefreshToken(Guid userId, Guid familyId, DateTime occurredAt)
        {
            var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
            var expiresAt = occurredAt.AddDays(_jwtOptions.RefreshTokenDays);

            return new RefreshTokenPair(
                rawToken,
                new RefreshToken
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    FamilyId = familyId,
                    TokenHash = HashRefreshToken(rawToken),
                    CreatedAt = occurredAt,
                    ExpiresAt = expiresAt
                });
        }

        private static string HashRefreshToken(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static bool IsBcryptHash(string value)
            => value.StartsWith("$2", StringComparison.Ordinal);

        private static IEnumerable<string> GetRoles(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .Select(userRole => userRole.Role!.Name);

        private static IEnumerable<string> GetPermissions(User user)
            => user.UserRoles
                .Where(userRole => userRole.Role != null)
                .SelectMany(userRole => userRole.Role!.RolePermissions)
                .Where(rolePermission => rolePermission.Permission != null)
                .Select(rolePermission => rolePermission.Permission!.Name);

        private static ApiException Unauthorized()
            => new(401, "unauthorized", "Tên đăng nhập, mật khẩu hoặc token không hợp lệ.");

        private sealed record RefreshTokenPair(string RawToken, RefreshToken Entity);
    }
}
