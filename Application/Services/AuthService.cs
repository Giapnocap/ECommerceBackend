using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Observability;
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
        private readonly IPasswordHasher _passwordHasher;
        private readonly IOutboxWriter _outbox;
        private readonly IAuditWriter _audit;
        private readonly JwtOptions _jwtOptions;
        private readonly AuthSecurityOptions _securityOptions;
        private readonly TimeProvider _timeProvider;

        public AuthService(
            IGenericRepository<User> userRepo,
            IGenericRepository<Role> roleRepo,
            IGenericRepository<UserRole> userRoleRepo,
            IGenericRepository<Cart> cartRepo,
            IGenericRepository<RefreshToken> refreshTokenRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IOptions<JwtOptions> jwtOptions,
            IOptions<AuthSecurityOptions> securityOptions,
            IPasswordHasher passwordHasher,
            IOutboxWriter outbox,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _userRepo = userRepo;
            _roleRepo = roleRepo;
            _userRoleRepo = userRoleRepo;
            _cartRepo = cartRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _context = context;
            _consistency = consistency;
            _passwordHasher = passwordHasher;
            _outbox = outbox;
            _audit = audit;
            _jwtOptions = jwtOptions.Value;
            _securityOptions = securityOptions.Value;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<AuthResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.register", cancellationToken);
            var occurredAt = UtcNow;
            var userName = request.UserName.Trim();
            var email = request.Email.Trim();
            var normalizedUserName = Normalize(userName);
            var normalizedEmail = Normalize(email);

            if (await _userRepo.Query().AnyAsync(
                user => user.NormalizedUserName == normalizedUserName,
                cancellationToken))
                throw new ConflictException("username_conflict", $"Tên đăng nhập '{userName}' đã tồn tại.");

            if (await _userRepo.Query().AnyAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken))
                throw new ConflictException("email_conflict", $"Email '{email}' đã được sử dụng.");

            var customerRole = await _roleRepo.Query()
                .Include(role => role.RolePermissions)
                    .ThenInclude(rolePermission => rolePermission.Permission)
                .FirstOrDefaultAsync(
                    role => role.Name == RoleNames.Customer,
                    cancellationToken)
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
                PasswordHash = _passwordHasher.Hash(request.Password),
                CreatedAt = occurredAt
            };

            await _userRepo.AddAsync(user, cancellationToken);
            await _userRoleRepo.AddAsync(
                new UserRole { UserId = user.Id, RoleId = customerRole.Id },
                cancellationToken);
            await _cartRepo.AddAsync(
                new Cart { Id = Guid.NewGuid(), UserId = user.Id },
                cancellationToken);

            var refreshToken = CreateRefreshToken(user.Id, Guid.NewGuid(), occurredAt);
            await _refreshTokenRepo.AddAsync(refreshToken.Entity, cancellationToken);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                throw new ConflictException(
                    "identity_conflict",
                    "Tên đăng nhập hoặc email đã được sử dụng bởi một yêu cầu khác.",
                    ex);
            }

            var response = BuildAuthResponse(
                user,
                [customerRole.Name],
                customerRole.RolePermissions
                    .Where(rolePermission => rolePermission.Permission != null)
                    .Select(rolePermission => rolePermission.Permission!.Name),
                refreshToken.RawToken,
                refreshToken.Entity.ExpiresAt,
                refreshToken.Entity.FamilyId,
                occurredAt);
            telemetry.Complete();
            return response;
        }

        public async Task<AuthResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.login", cancellationToken);
            var normalizedUserName = Normalize(request.UserName);
            var userId = await _userRepo.Query()
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.NormalizedUserName == normalizedUserName)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (!userId.HasValue)
            {
                _ = _passwordHasher.Verify(request.Password, null);
                throw Unauthorized();
            }

            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    userId.Value,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw Unauthorized();

                var occurredAt = UtcNow;
                var passwordValid = _passwordHasher.Verify(
                    request.Password,
                    user.PasswordHash);
                if (user.IsLockedOutAt(occurredAt))
                {
                    telemetry.SetTag("auth.account.locked", true);
                    throw Unauthorized();
                }

                if (!passwordValid)
                {
                    var locked = DomainRuleGuard.AsConflict(() =>
                        user.RecordFailedLogin(
                            occurredAt,
                            _securityOptions.MaxFailedLoginAttempts,
                            TimeSpan.FromMinutes(_securityOptions.LockoutMinutes)));
                    if (locked)
                    {
                        telemetry.SetTag("auth.account.locked", true);
                        _audit.Write(
                            "auth.account.locked",
                            nameof(User),
                            user.Id.ToString(),
                            metadata: new Dictionary<string, object?>
                            {
                                ["lockoutMinutes"] = _securityOptions.LockoutMinutes,
                                ["failedAttempts"] = user.FailedLoginCount
                            });
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    transactionCompleted = true;
                    throw Unauthorized();
                }

                user.ClearLoginFailures();
                await LoadRolesAndPermissionsAsync(user, cancellationToken);
                var refreshToken = CreateRefreshToken(user.Id, Guid.NewGuid(), occurredAt);
                await _refreshTokenRepo.AddAsync(refreshToken.Entity, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = BuildAuthResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    refreshToken.RawToken,
                    refreshToken.Entity.ExpiresAt,
                    refreshToken.Entity.FamilyId,
                    occurredAt);
                telemetry.Complete();
                return response;
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.password_reset.request",
                cancellationToken);
            var normalizedEmail = Normalize(request.Email);
            var userId = await _userRepo.Query()
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.NormalizedEmail == normalizedEmail)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (!userId.HasValue)
            {
                telemetry.Complete();
                return;
            }

            await using var transaction = await _consistency.BeginTransactionAsync(
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

                var occurredAt = UtcNow;
                await RevokePasswordResetTokensAsync(
                    user.Id,
                    exceptTokenId: null,
                    occurredAt,
                    cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                var rawToken = Convert.ToBase64String(
                    RandomNumberGenerator.GetBytes(48));
                _context.PasswordResetTokens.Add(new PasswordResetToken
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    TokenHash = HashPasswordResetToken(rawToken),
                    CreatedAt = occurredAt,
                    ExpiresAt = occurredAt.AddMinutes(
                        _securityOptions.PasswordResetTokenMinutes)
                });
                _outbox.EnqueueSensitiveNotification(
                    user.Id,
                    "Đặt lại mật khẩu",
                    BuildPasswordResetMessage(rawToken));
                _audit.Write(
                    "auth.password_reset.requested",
                    nameof(User),
                    user.Id.ToString());

                await _context.SaveChangesAsync(cancellationToken);
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

        public async Task ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start(
                "auth.password_reset.complete",
                cancellationToken);
            var tokenHash = HashPasswordResetToken(request.Token.Trim());
            var userId = await _context.PasswordResetTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (Guid?)token.UserId)
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw InvalidPasswordResetToken();

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
                    ?? throw InvalidPasswordResetToken();
                var token = await _context.PasswordResetTokens
                    .SingleOrDefaultAsync(
                        candidate => candidate.TokenHash == tokenHash,
                        cancellationToken)
                    ?? throw InvalidPasswordResetToken();
                var occurredAt = UtcNow;
                if (!token.IsActiveAt(occurredAt))
                    throw InvalidPasswordResetToken();

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

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw InvalidPasswordResetToken();
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "password_reset_concurrency_conflict",
                    "Mật khẩu hoặc mã đặt lại đang được xử lý bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                if (!transactionCompleted)
                    await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task<AuthResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.refresh", cancellationToken);
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var tokenOwnerId = await FindRefreshTokenOwnerIdAsync(tokenHash, cancellationToken)
                ?? throw Unauthorized();
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var transactionCompleted = false;

            try
            {
                var user = await _consistency.LockUserAsync(
                    tokenOwnerId,
                    activeOnly: true,
                    cancellationToken)
                    ?? throw Unauthorized();
                var storedToken = await LoadRefreshTokenForUpdateAsync(tokenHash, cancellationToken)
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
                            occurredAt,
                            cancellationToken);
                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        transactionCompleted = true;
                    }

                    throw Unauthorized();
                }

                if (storedToken.IsExpiredAt(occurredAt))
                    throw Unauthorized();

                await LoadRolesAndPermissionsAsync(user, cancellationToken);
                var newRefreshToken = CreateRefreshToken(user.Id, storedToken.FamilyId, occurredAt);
                DomainRuleGuard.AsConflict(() =>
                    storedToken.Rotate(occurredAt, newRefreshToken.Entity.TokenHash));
                await _refreshTokenRepo.AddAsync(newRefreshToken.Entity, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;

                var response = BuildAuthResponse(
                    user,
                    GetRoles(user),
                    GetPermissions(user),
                    newRefreshToken.RawToken,
                    newRefreshToken.Entity.ExpiresAt,
                    newRefreshToken.Entity.FamilyId,
                    occurredAt);
                telemetry.Complete();
                return response;
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

        public async Task LogoutAsync(
            Guid userId,
            LogoutRequest request,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.logout", cancellationToken);
            var tokenHash = HashRefreshToken(request.RefreshToken);
            await using var transaction = await _consistency.BeginTransactionAsync(
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
                    var storedToken = await LoadRefreshTokenForUpdateAsync(
                        tokenHash,
                        cancellationToken);
                    if (storedToken != null && storedToken.UserId == user.Id)
                    {
                        await RevokeTokenFamilyAsync(
                            user.Id,
                            storedToken.FamilyId,
                            "Logout",
                            UtcNow,
                            cancellationToken);
                        await _context.SaveChangesAsync(cancellationToken);
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

        public async Task LogoutAllAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            using var telemetry = BusinessTelemetry.Start("auth.logout_all", cancellationToken);
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
                var occurredAt = UtcNow;
                await RevokeAllUserTokensAsync(
                    user.Id,
                    "Logout all",
                    occurredAt,
                    cancellationToken);
                DomainRuleGuard.AsConflict(user.InvalidateSessions);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                transactionCompleted = true;
                telemetry.Complete();
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

        private async Task<Guid?> FindRefreshTokenOwnerIdAsync(
            string tokenHash,
            CancellationToken cancellationToken)
            => await _context.RefreshTokens
                .AsNoTracking()
                .Where(token => token.TokenHash == tokenHash)
                .Select(token => (Guid?)token.UserId)
                .SingleOrDefaultAsync(cancellationToken);

        private async Task<RefreshToken?> LoadRefreshTokenForUpdateAsync(
            string tokenHash,
            CancellationToken cancellationToken)
            => await _consistency.LockRefreshTokenAsync(tokenHash, cancellationToken);

        private async Task RevokeTokenFamilyAsync(
            Guid userId,
            Guid familyId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens = await _context.RefreshTokens
                .Where(token => token.UserId == userId
                    && token.FamilyId == familyId
                    && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            RevokeTokens(tokens, reason, occurredAt);
        }

        private async Task RevokeAllUserTokensAsync(
            Guid userId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens = await _context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(cancellationToken);
            RevokeTokens(tokens, reason, occurredAt);
        }

        private async Task RevokePasswordResetTokensAsync(
            Guid userId,
            Guid? exceptTokenId,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens = await _context.PasswordResetTokens
                .Where(token => token.UserId == userId
                    && token.ConsumedAt == null
                    && token.RevokedAt == null
                    && (!exceptTokenId.HasValue || token.Id != exceptTokenId.Value))
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt));
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

        private async Task LoadRolesAndPermissionsAsync(
            User user,
            CancellationToken cancellationToken)
            => await _context.Entry(user)
                .Collection(candidate => candidate.UserRoles)
                .Query()
                .Include(userRole => userRole.Role)
                    .ThenInclude(role => role!.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
                .LoadAsync(cancellationToken);
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

        private static string HashPasswordResetToken(string token)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

        private string BuildPasswordResetMessage(string rawToken)
        {
            var url = new UriBuilder(_securityOptions.PasswordResetUrl)
            {
                Query = $"token={Uri.EscapeDataString(rawToken)}"
            }.Uri.AbsoluteUri;
            return $"Mở liên kết sau để đặt lại mật khẩu: {url}\n"
                + $"Liên kết có hiệu lực trong {_securityOptions.PasswordResetTokenMinutes} phút.";
        }

        private static string Normalize(string value) => value.Trim().ToUpperInvariant();

        private static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        private static ApiException InvalidPasswordResetToken()
            => new(
                400,
                "invalid_password_reset_token",
                "Mã đặt lại mật khẩu không hợp lệ hoặc đã hết hạn.");

        private sealed record RefreshTokenPair(string RawToken, RefreshToken Entity);
    }
}
