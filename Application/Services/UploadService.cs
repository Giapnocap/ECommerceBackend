using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public class UploadService : IUploadService
    {
        private const string UploadsDirectory = "Uploads";
        private const string ProductImagesDirectory = "products";

        private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png", ".webp"];

        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<UploadService> _logger;
        private readonly long _maxImageSizeBytes;
        private readonly IAuditWriter _audit;

        public UploadService(
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IWebHostEnvironment environment,
            IOptions<UploadOptions> options,
            ILogger<UploadService> logger,
            IAuditWriter? auditWriter = null)
        {
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _environment = environment;
            _logger = logger;
            _maxImageSizeBytes = options.Value.MaxImageSizeBytes;
            _audit = auditWriter ?? NullAuditWriter.Instance;
        }

        public async Task<UploadImageResponse> UploadProductImageAsync(
            Guid productId,
            IFormFile file,
            bool isMain,
            CancellationToken cancellationToken = default,
            Guid? actorUserId = null)
        {
            var productExists =
                await _productRepository.ActiveProductExistsAsync(
                    productId,
                    cancellationToken);
            if (!productExists)
                throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{productId}'.");

            var extension = await ValidateImageAsync(file, cancellationToken);
            var uploadsFolder = GetProductImagesFolder();
            Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            try
            {
                await using (var stream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                return await PersistProductImageAsync(
                    productId,
                    fileName,
                    isMain,
                    cancellationToken,
                    actorUserId);
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                DeleteGeneratedFile(filePath);
                _logger.LogWarning(ex, "Could not persist product image for product {ProductId}.", productId);
                throw ImageConcurrencyConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                DeleteGeneratedFile(filePath);
                throw ImageConcurrencyConflict(ex);
            }
            catch
            {
                DeleteGeneratedFile(filePath);
                throw;
            }
        }

        public async Task DeleteProductImageAsync(
            Guid productId,
            Guid imageId,
            CancellationToken cancellationToken = default,
            Guid? actorUserId = null)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                cancellationToken);
            string imageUrl;

            try
            {
                _ = await _consistency.LockProductAsync(productId, activeOnly: true, cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{productId}'.");
                var image = await _productRepository.GetImageAsync(
                    productId,
                    imageId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy ảnh sản phẩm.");
                imageUrl = image.ImageUrl;

                ProductImage? replacement = null;
                if (image.IsMain)
                {
                    replacement =
                        await _productRepository.GetReplacementImageAsync(
                            productId,
                            imageId,
                            cancellationToken);
                }

                _productRepository.RemoveImage(image);
                _audit.Write(
                    "product.image.delete",
                    "ProductImage",
                    image.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?> { ["productId"] = productId });
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                if (replacement != null)
                {
                    replacement.IsMain = true;
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw ImageConcurrencyConflict(ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw ImageConcurrencyConflict(ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            DeleteStoredFile(imageUrl);
        }
        private async Task<UploadImageResponse> PersistProductImageAsync(
            Guid productId,
            string fileName,
            bool isMain,
            CancellationToken cancellationToken,
            Guid? actorUserId)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                cancellationToken);
            try
            {
                var product = await _consistency.LockProductAsync(
                        productId,
                        activeOnly: true,
                        cancellationToken)
                    ?? throw new NotFoundException($"Không tìm thấy sản phẩm với Id '{productId}'.");
                await _productRepository.LoadImagesAsync(
                    product,
                    cancellationToken);

                var imageIsMain = isMain || !product.Images.Any(image => image.IsMain);
                if (imageIsMain)
                {
                    var currentMainImages = product.Images.Where(image => image.IsMain).ToList();
                    foreach (var currentMain in currentMainImages)
                        currentMain.IsMain = false;

                    if (currentMainImages.Count > 0)
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                var image = new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = productId,
                    ImageUrl = $"/uploads/{ProductImagesDirectory}/{fileName}",
                    IsMain = imageIsMain
                };

                await _productRepository.AddImageAsync(image, cancellationToken);
                _audit.Write(
                    "product.image.upload",
                    "ProductImage",
                    image.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["productId"] = productId,
                        ["isMain"] = image.IsMain
                    });
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return image.ToUploadImageResponse();
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
        private async Task<string> ValidateImageAsync(IFormFile file, CancellationToken cancellationToken)
        {
            if (file == null || file.Length == 0)
                throw new BusinessException("Vui lòng chọn tệp ảnh để tải lên.");

            if (file.Length > _maxImageSizeBytes)
            {
                var maxSizeMb = _maxImageSizeBytes / (1024d * 1024d);
                throw new BusinessException($"Kích thước ảnh không được vượt quá {maxSizeMb:0.##} MB.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
                throw new BusinessException("Chỉ chấp nhận tệp ảnh định dạng JPG, JPEG, PNG hoặc WEBP.");

            var expectedContentType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => string.Empty
            };

            if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
                throw new BusinessException("Phần mở rộng và kiểu nội dung của tệp ảnh không khớp.");

            var header = new byte[12];
            var bytesRead = 0;
            await using var input = file.OpenReadStream();
            while (bytesRead < header.Length)
            {
                var read = await input.ReadAsync(header.AsMemory(bytesRead), cancellationToken);
                if (read == 0)
                    break;

                bytesRead += read;
            }

            if (!HasValidSignature(extension, header, bytesRead))
                throw new BusinessException("Nội dung tệp không đúng định dạng ảnh được khai báo.");

            return extension;
        }

        private static bool HasValidSignature(string extension, byte[] header, int bytesRead)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" => bytesRead >= 3
                    && header[0] == 0xFF
                    && header[1] == 0xD8
                    && header[2] == 0xFF,
                ".png" => bytesRead >= 8
                    && header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
                ".webp" => bytesRead >= 12
                    && header.AsSpan(0, 4).SequenceEqual("RIFF"u8)
                    && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
                _ => false
            };
        }

        private static ConflictException ImageConcurrencyConflict(Exception exception)
            => new(
                "product_image_concurrency_conflict",
                "Ảnh sản phẩm đang được cập nhật bởi yêu cầu khác. Vui lòng thử lại.",
                exception);

        private string GetProductImagesFolder()
            => Path.Combine(_environment.ContentRootPath, UploadsDirectory, ProductImagesDirectory);

        private void DeleteStoredFile(string imageUrl)
        {
            var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, UploadsDirectory));
            var relativePath = imageUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, relativePath));
            var uploadsPrefix = uploadsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Skipped deleting unsafe upload path {ImageUrl}.", imageUrl);
                return;
            }

            DeleteGeneratedFile(fullPath);
        }

        private void DeleteGeneratedFile(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete image file {FilePath}.", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Could not delete image file {FilePath}.", filePath);
            }
        }
    }
}
