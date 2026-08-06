namespace ECommerceBackend.Application.Interfaces
{
    public interface IProductImageStorage
    {
        Task SaveAsync(
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteByUrlAsync(
            string imageUrl,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteByNameAsync(
            string fileName,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StoredProductImageFile>> GetFilesAsync(
            CancellationToken cancellationToken = default);
    }

    public interface IProductImageStorageHealthProbe
    {
        Task CheckAvailabilityAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed record StoredProductImageFile(
        string Name,
        DateTime LastWriteTimeUtc);
}
