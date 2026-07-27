using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> UserNameExistsAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default)
            => _context.Users.AnyAsync(
                user => user.NormalizedUserName == normalizedUserName,
                cancellationToken);

        public Task<bool> EmailExistsAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default)
            => _context.Users.AnyAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken);

        public Task<Guid?> GetActiveUserIdByUserNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default)
            => _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted
                    && user.NormalizedUserName == normalizedUserName)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<Guid?> GetActiveUserIdByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default)
            => _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted
                    && user.NormalizedEmail == normalizedEmail)
                .Select(user => (Guid?)user.Id)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<string?> GetActiveEmailAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _context.Users
                .AsNoTracking()
                .Where(user => user.Id == userId && !user.IsDeleted)
                .Select(user => user.Email)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<User?> GetProfileAsync(
            Guid userId,
            bool tracking,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Users.AsQueryable();
            if (!tracking)
                query = query.AsNoTracking();
            return query
                .Include(candidate => candidate.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .FirstOrDefaultAsync(
                    candidate => !candidate.IsDeleted
                        && candidate.Id == userId,
                    cancellationToken);
        }

        public async Task<PageSlice<User>> GetPageAsync(
            UserQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted);

            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                var normalizedKeyword = keyword.ToUpperInvariant();
                query = query.Where(user =>
                    user.NormalizedUserName.Contains(normalizedKeyword)
                    || user.NormalizedEmail.Contains(normalizedKeyword)
                    || user.FullName.Contains(keyword));
            }

            if (!string.IsNullOrWhiteSpace(queryParams.Role))
            {
                var role = queryParams.Role.Trim();
                query = query.Where(user =>
                    user.UserRoles.Any(userRole =>
                        userRole.Role != null
                        && userRole.Role.Name == role));
            }

            var totalCount = await query.CountAsync(cancellationToken);
            var users = await query
                .OrderBy(user => user.FullName)
                .ThenBy(user => user.Id)
                .Skip(skip)
                .Take(take)
                .Include(user => user.UserRoles)
                    .ThenInclude(userRole => userRole.Role)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            return new PageSlice<User>(users, totalCount);
        }

        public Task<Role?> GetRoleAsync(
            string roleName,
            bool includePermissions,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Roles.AsQueryable();
            if (includePermissions)
            {
                query = query
                    .Include(role => role.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission);
            }
            return query.FirstOrDefaultAsync(
                role => role.Name == roleName,
                cancellationToken);
        }

        public Task<int> CountActiveAdminsAsync(
            CancellationToken cancellationToken = default)
            => _context.UserRoles.CountAsync(
                userRole => userRole.Role != null
                    && userRole.Role.Name == RoleNames.Admin
                    && userRole.User != null
                    && !userRole.User.IsDeleted,
                cancellationToken);

        public Task LoadRolesAsync(
            User user,
            bool includePermissions,
            CancellationToken cancellationToken = default)
        {
            var query = _context.Entry(user)
                .Collection(candidate => candidate.UserRoles)
                .Query()
                .Include(userRole => userRole.Role);
            if (includePermissions)
            {
                return query
                    .ThenInclude(role => role!.RolePermissions)
                        .ThenInclude(rolePermission => rolePermission.Permission)
                    .LoadAsync(cancellationToken);
            }
            return query.LoadAsync(cancellationToken);
        }

        public Task AddAsync(
            User user,
            CancellationToken cancellationToken = default)
            => _context.Users.AddAsync(user, cancellationToken).AsTask();

        public Task AddRoleAsync(
            UserRole userRole,
            CancellationToken cancellationToken = default)
            => _context.UserRoles.AddAsync(userRole, cancellationToken).AsTask();

        public void RemoveRole(UserRole userRole)
            => _context.UserRoles.Remove(userRole);
    }
}
