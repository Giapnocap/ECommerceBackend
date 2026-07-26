using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;

        public UserService(
            IAppDbContext context,
            IDataConsistencyService consistency)
            : this(
                context,
                consistency,
                TimeProvider.System)
        {
        }

        public UserService(
            IAppDbContext context,
            IDataConsistencyService consistency,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _context = context;
            _consistency = consistency;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<UserResponse> GetProfileAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .AsNoTracking()
                .Include(candidate => candidate.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .FirstOrDefaultAsync(
                    candidate => !candidate.IsDeleted && candidate.Id == userId,
                    cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");

            return user.ToResponse();
        }

        public async Task<UserResponse> UpdateProfileAsync(
            Guid userId,
            UpdateProfileRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = await _context.Users
                .Include(candidate => candidate.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .FirstOrDefaultAsync(
                    candidate => !candidate.IsDeleted && candidate.Id == userId,
                    cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");

            user.FullName = request.FullName.Trim();
            user.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConflictException("Thông tin người dùng vừa được cập nhật bởi yêu cầu khác.", ex);
            }

            return user.ToResponse();
        }

        public async Task ChangePasswordAsync(
            Guid userId,
            ChangePasswordRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

            try
            {
                var user = await LoadUserForUpdateAsync(userId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");

                if (!user.PasswordHash.StartsWith("$2", StringComparison.Ordinal)
                    || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    throw new BusinessException("password_current_invalid", "Mật khẩu hiện tại không đúng.");
                }

                if (BCrypt.Net.BCrypt.Verify(request.NewPassword, user.PasswordHash))
                    throw new BusinessException("password_reuse_forbidden", "Mật khẩu mới phải khác mật khẩu hiện tại.");

                var occurredAt = UtcNow;
                DomainRuleGuard.AsConflict(() => user.ChangePasswordHash(
                    BCrypt.Net.BCrypt.HashPassword(request.NewPassword),
                    occurredAt));
                await RevokeAllRefreshTokensAsync(
                    user.Id,
                    "Password changed",
                    occurredAt,
                    cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Tài khoản vừa được cập nhật bởi yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "session_concurrency_conflict",
                    "Tài khoản đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        public async Task<PagedResult<UserResponse>> GetAllUsersAsync(
            UserQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(queryParams.Page, queryParams.PageSize, defaultSize: 20);
            var query = _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted);

            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                var normalizedKeyword = keyword.ToUpperInvariant();
                query = query.Where(user => user.NormalizedUserName.Contains(normalizedKeyword)
                    || user.NormalizedEmail.Contains(normalizedKeyword)
                    || user.FullName.Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Role))
            {
                var role = queryParams.Role.Trim();
                query = query.Where(user => user.UserRoles.Any(userRole => userRole.Role != null
                    && userRole.Role.Name == role));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var users = await query
                .OrderBy(user => user.FullName)
                .ThenBy(user => user.Id)
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);

            return PagedResult<UserResponse>.Create(
                users.Select(user => user.ToResponse()),
                totalCount,
                paging.Page,
                paging.Size);
        }

        public async Task AssignRoleAsync(
            Guid actorUserId,
            Guid userId,
            AssignRoleRequest request,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var user = await LoadUserForUpdateAsync(userId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");
                await _context.Entry(user)
                    .Collection(candidate => candidate.UserRoles)
                    .Query()
                    .Include(userRole => userRole.Role)
                    .LoadAsync(cancellationToken);

                var role = await _context.Roles
                    .FirstOrDefaultAsync(
                        candidate => candidate.Name == request.RoleName,
                        cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy vai trò được yêu cầu.");
                var currentRoles = user.UserRoles.ToList();

                if (currentRoles.Count == 1 && currentRoles[0].RoleId == role.Id)
                {
                    await transaction.CommitAsync(cancellationToken);

                    return;
                }

                if (actorUserId == userId)
                    throw new BusinessException("role_self_change_forbidden", "Không thể thay đổi vai trò của chính mình.");

                var currentlyAdmin = currentRoles.Any(userRole => userRole.Role?.Name == RoleNames.Admin);
                if (currentlyAdmin && role.Name != RoleNames.Admin)
                {
                    var activeAdminCount = await _context.UserRoles
                        .CountAsync(userRole => userRole.Role != null
                            && userRole.Role.Name == RoleNames.Admin
                            && userRole.User != null
                            && !userRole.User.IsDeleted,
                            cancellationToken);

                    if (activeAdminCount <= 1)
                    {
                        var message = actorUserId == userId
                            ? "Không thể tự hạ quyền của quản trị viên cuối cùng."
                            : "Không thể hạ quyền của quản trị viên cuối cùng.";
                        throw new BusinessException("last_admin_demotion_forbidden", message);
                    }
                }

                foreach (var userRole in currentRoles)
                    _context.UserRoles.Remove(userRole);

                await _context.UserRoles.AddAsync(
                    new UserRole { UserId = userId, RoleId = role.Id },
                    cancellationToken);
                var occurredAt = UtcNow;
                DomainRuleGuard.AsConflict(user.InvalidateSessions);
                await RevokeAllRefreshTokensAsync(
                    user.Id,
                    "Role changed",
                    occurredAt,
                    cancellationToken);
                _audit.Write(
                    "user.role.assign",
                    "User",
                    user.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["previousRoles"] = currentRoles
                            .Select(userRole => userRole.Role?.Name)
                            .Where(name => name != null)
                            .ToArray(),
                        ["assignedRole"] = role.Name
                    });
                await _context.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException("Vai trò người dùng vừa được cập nhật bởi yêu cầu khác.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw new ConflictException(
                    "role_concurrency_conflict",
                    "Vai trò người dùng đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                    ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);

                throw;
            }
        }

        private async Task<User?> LoadUserForUpdateAsync(
            Guid userId,
            CancellationToken cancellationToken)
            => await _consistency.LockUserAsync(
                userId,
                activeOnly: true,
                cancellationToken);

        private async Task RevokeAllRefreshTokensAsync(
            Guid userId,
            string reason,
            DateTime occurredAt,
            CancellationToken cancellationToken)
        {
            var tokens = await _context.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt, reason));
            }
        }
    }
}
