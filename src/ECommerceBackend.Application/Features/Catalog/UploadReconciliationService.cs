using System.Diagnostics.Metrics;
using System.Text.RegularExpressions;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed partial class UploadReconciliationService : IUploadReconciliationService
    {
        private const string RelativePrefix = "/uploads/products/";
        private const int MaxReportedPaths = 100;
        private static readonly Meter Meter = new("ECommerceBackend.Operations");
        private static readonly Counter<long> DeletedCounter = Meter.CreateCounter<long>("uploads.orphans.deleted");

        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IProductImageStorage _imageStorage;
        private readonly IAuditWriter _audit;
        private readonly UploadOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<UploadReconciliationService> _logger;

        public UploadReconciliationService(
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IProductImageStorage imageStorage,
            IAuditWriter audit,
            IOptions<UploadOptions> options,
            TimeProvider timeProvider,
            ILogger<UploadReconciliationService> logger)
        {
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _imageStorage = imageStorage;
            _audit = audit;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<UploadReconciliationResponse> ReconcileAsync(
            UploadReconciliationRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            var maxDeletes = Math.Clamp(request.MaxDeletes, 1, _options.MaxReconciliationDeletes);

            var referencedUrls =
                await _productRepository.GetImageUrlsAsync(
                    cancellationToken);
            var referencedNames = referencedUrls
                .Where(url => url.StartsWith(RelativePrefix, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var files = await _imageStorage.GetFilesAsync(cancellationToken);
            var diskNames = files.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = referencedNames
                .Where(name => !diskNames.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var orphans = files
                .Where(file => !referencedNames.Contains(file.Name))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var cutoff = _timeProvider.GetUtcNow().UtcDateTime
                .AddMinutes(-_options.ReconciliationGraceMinutes);
            var eligible = orphans
                .Where(file => GeneratedImageName().IsMatch(file.Name)
                    && file.LastWriteTimeUtc <= cutoff)
                .ToList();
            var deleteCandidates = eligible.Take(maxDeletes).ToList();

            var deleted = 0;
            if (request.DeleteOrphans)
            {
                foreach (var file in deleteCandidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (await _imageStorage.DeleteByNameAsync(
                            file.Name,
                            cancellationToken))
                            deleted++;
                    }
                    catch (IOException ex)
                    {
                        _logger.LogWarning(ex, "Could not delete orphan upload {FileName}.", file.Name);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning(ex, "Could not delete orphan upload {FileName}.", file.Name);
                    }
                }

                if (deleted > 0)
                {
                    _audit.Write(
                        "uploads.orphans.delete",
                        "ProductImage",
                        null,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["deletedCount"] = deleted,
                            ["eligibleCount"] = eligible.Count,
                            ["batchSize"] = deleteCandidates.Count,
                            ["graceMinutes"] = _options.ReconciliationGraceMinutes
                        });
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    DeletedCounter.Add(deleted);
                }
            }

            return new UploadReconciliationResponse
            {
                DryRun = !request.DeleteOrphans,
                ScannedFileCount = files.Count,
                ReferencedFileCount = referencedNames.Count,
                MissingFileCount = missing.Count,
                OrphanFileCount = orphans.Count,
                EligibleOrphanCount = eligible.Count,
                DeletedFileCount = deleted,
                MissingFiles = missing.Take(MaxReportedPaths).Select(ToRelativeUrl).ToList(),
                OrphanFiles = orphans.Take(MaxReportedPaths).Select(file => ToRelativeUrl(file.Name)).ToList()
            };
        }

        private static string ToRelativeUrl(string fileName) => $"{RelativePrefix}{fileName}";

        [GeneratedRegex("^[0-9a-f]{32}\\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex GeneratedImageName();
    }
}
