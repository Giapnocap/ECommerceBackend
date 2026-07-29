using System.Data;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IAuthSessionRepository _authSessionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly TimeProvider _timeProvider;
        private readonly IAuditWriter _audit;

        public UserService(
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency)
            : this(
                userRepository,
                authSessionRepository,
                unitOfWork,
                consistency,
                TimeProvider.System)
        {
        }

        public UserService(
            IUserRepository userRepository,
            IAuthSessionRepository authSessionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            TimeProvider timeProvider,
            IAuditWriter? auditWriter = null)
        {
            _userRepository = userRepository;
            _authSessionRepository = authSessionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _timeProvider = timeProvider;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<UserResponse> GetProfileAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetProfileAsync(
                userId,
                tracking: false,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");

            return user.ToResponse();
        }

        public async Task<UserResponse> UpdateProfileAsync(
            Guid userId,
            UpdateProfileRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = await _userRepository.GetProfileAsync(
                userId,
                tracking: true,
                cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy người dùng.");

            DomainRuleGuard.AsBusiness(() =>
                user.UpdateProfile(
                    request.FullName,
                    request.Phone));

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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
            var result = await _userRepository.GetPageAsync(
                queryParams,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);

            return PagedResult<UserResponse>.Create(
                result.Items.Select(user => user.ToResponse()),
                result.TotalCount,
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
                if (!await _consistency.TryAcquireRoleAssignmentLockAsync(
                    cancellationToken))
                {
                    throw new ConflictException(
                        "role_concurrency_conflict",
                        "Không thể khóa thao tác phân quyền. Vui lòng thử lại.");
                }

                var user = await LoadUserForUpdateAsync(userId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy người dùng.");
                await _userRepository.LoadRolesAsync(
                    user,
                    includePermissions: false,
                    cancellationToken);

                var role = await _userRepository.GetRoleAsync(
                    request.RoleName,
                    includePermissions: false,
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
                    var activeAdminCount =
                        await _userRepository.CountActiveAdminsAsync(
                            cancellationToken);

                    if (activeAdminCount <= 1)
                    {
                        var message = actorUserId == userId
                            ? "Không thể tự hạ quyền của quản trị viên cuối cùng."
                            : "Không thể hạ quyền của quản trị viên cuối cùng.";
                        throw new BusinessException("last_admin_demotion_forbidden", message);
                    }
                }

                var roleChange = DomainRuleGuard.AsConflict(() =>
                    user.ChangeRole(role));
                foreach (var userRole in roleChange.PreviousAssignments)
                    _userRepository.RemoveRole(userRole);

                await _userRepository.AddRoleAsync(
                    roleChange.Assignment,
                    cancellationToken);
                var occurredAt = UtcNow;
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
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
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
            var tokens =
                await _authSessionRepository.GetActiveRefreshTokensAsync(
                    userId,
                    cancellationToken);

            foreach (var token in tokens)
            {
                DomainRuleGuard.AsConflict(() => token.Revoke(occurredAt, reason));
            }
        }
    }
}
