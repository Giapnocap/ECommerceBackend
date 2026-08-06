using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace ECommerceBackend.Infrastructure.Storage
{
    public sealed class LocalProductImageStorage :
        IProductImageStorage,
        IProductImageStorageHealthProbe
    {
        private const string UploadsDirectory = "Uploads";
        private const string ProductImagesDirectory = "products";
        private static readonly byte[] AvailabilityProbePayload = [0];

        private readonly string _contentRootPath;
        private readonly string _uploadsRootPath;
        private readonly string _productImagesPath;
        private readonly ILogger<LocalProductImageStorage> _logger;

        public LocalProductImageStorage(
            IWebHostEnvironment environment,
            ILogger<LocalProductImageStorage> logger)
        {
            _contentRootPath = environment.ContentRootPath;
            _uploadsRootPath = Path.GetFullPath(
                Path.Combine(_contentRootPath, UploadsDirectory));
            _productImagesPath = Path.Combine(
                _uploadsRootPath,
                ProductImagesDirectory);
            _logger = logger;
        }

        public async Task SaveAsync(
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default)
        {
            var filePath = ResolveProductImagePath(fileName);
            Directory.CreateDirectory(_productImagesPath);

            await using var output = new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            await content.CopyToAsync(output, cancellationToken);
        }

        public Task<bool> DeleteByUrlAsync(
            string imageUrl,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = imageUrl
                .TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(
                Path.Combine(_contentRootPath, relativePath));
            var uploadsPrefix = _uploadsRootPath.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Skipped deleting unsafe upload path {ImageUrl}.",
                    imageUrl);
                return Task.FromResult(false);
            }

            File.Delete(fullPath);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteByNameAsync(
            string fileName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ResolveProductImagePath(fileName);
            File.Delete(filePath);
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<StoredProductImageFile>> GetFilesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_productImagesPath);
            var files = new List<StoredProductImageFile>();
            foreach (var path in Directory.EnumerateFiles(
                _productImagesPath,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = new FileInfo(path);
                files.Add(new StoredProductImageFile(
                    file.Name,
                    file.LastWriteTimeUtc));
            }

            return Task.FromResult<IReadOnlyList<StoredProductImageFile>>(files);
        }

        public async Task CheckAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_productImagesPath);

            var probePath = Path.Combine(
                _productImagesPath,
                $".storage-probe-{Guid.NewGuid():N}.tmp");
            await using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            await probe.WriteAsync(AvailabilityProbePayload, cancellationToken);
            await probe.FlushAsync(cancellationToken);
        }

        private string ResolveProductImagePath(string fileName)
        {
            if (!string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Product image file name is invalid.");
            }

            return Path.Combine(_productImagesPath, fileName);
        }
    }
}
