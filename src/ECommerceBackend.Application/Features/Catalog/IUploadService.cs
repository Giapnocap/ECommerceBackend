using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IUploadFile
    {
        string FileName { get; }
        string ContentType { get; }
        long Length { get; }
        Stream OpenReadStream();
    }

    public interface IUploadService
    {
        Task<UploadImageResponse> UploadProductImageAsync(
            Guid productId,
            IUploadFile file,
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
