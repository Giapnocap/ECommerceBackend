using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IUserRepository
    {
        Task<bool> UserNameExistsAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default);

        Task<bool> EmailExistsAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default);

        Task<Guid?> GetActiveUserIdByUserNameAsync(
            string normalizedUserName,
            CancellationToken cancellationToken = default);

        Task<Guid?> GetActiveUserIdByEmailAsync(
            string normalizedEmail,
            CancellationToken cancellationToken = default);

        Task<string?> GetActiveEmailAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<User?> GetProfileAsync(
            Guid userId,
            bool tracking,
            CancellationToken cancellationToken = default);

        Task<PageSlice<User>> GetPageAsync(
            UserQueryParams queryParams,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<Role?> GetRoleAsync(
            string roleName,
            bool includePermissions,
            CancellationToken cancellationToken = default);

        Task<int> CountActiveAdminsAsync(
            CancellationToken cancellationToken = default);

        Task LoadRolesAsync(
            User user,
            bool includePermissions,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            User user,
            CancellationToken cancellationToken = default);

        Task AddRoleAsync(
            UserRole userRole,
            CancellationToken cancellationToken = default);

        void RemoveRole(UserRole userRole);
    }
}
