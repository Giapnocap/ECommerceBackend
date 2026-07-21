using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IUploadService
    {
        Task<UploadImageResponse> UploadProductImageAsync(
            Guid productId,
            IFormFile file,
            bool isMain,
            CancellationToken cancellationToken = default,
            Guid? actorUserId = null);

        Task DeleteProductImageAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default,
            Guid? actorUserId = null);
    }
}
