using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IUserService
    {
        Task<UserResponse> GetProfileAsync(Guid userId);
        Task<UserResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);
        Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
        Task<PagedResult<UserResponse>> GetAllUsersAsync(
            UserQueryParams queryParams,
            CancellationToken cancellationToken = default);
        Task AssignRoleAsync(Guid actorUserId, Guid userId, AssignRoleRequest request);
    }
}
