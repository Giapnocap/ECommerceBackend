using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IUserService
    {
        Task<UserResponse> GetProfileAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
        Task<UserResponse> UpdateProfileAsync(
            Guid userId,
            UpdateProfileRequest request,
            CancellationToken cancellationToken = default);
        Task ChangePasswordAsync(
            Guid userId,
            ChangePasswordRequest request,
            CancellationToken cancellationToken = default);
        Task<PagedResult<UserResponse>> GetAllUsersAsync(
            UserQueryParams queryParams,
            CancellationToken cancellationToken = default);
        Task AssignRoleAsync(
            Guid actorUserId,
            Guid userId,
            AssignRoleRequest request,
            CancellationToken cancellationToken = default);
    }
}
