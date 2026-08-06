using System.Diagnostics;
using ECommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Health
{
    public sealed class ProductImageStorageHealthCheck : IHealthCheck
    {
        private readonly IProductImageStorageHealthProbe _storage;
        private readonly ILogger<ProductImageStorageHealthCheck> _logger;

        public ProductImageStorageHealthCheck(
            IProductImageStorageHealthProbe storage,
            ILogger<ProductImageStorageHealthCheck> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                await _storage.CheckAvailabilityAsync(cancellationToken);

                return HealthCheckResult.Healthy(
                    "Kho lưu ảnh sản phẩm đang hoạt động.",
                    CreateData(startedAt));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Product image storage health check failed.");
                return HealthCheckResult.Unhealthy(
                    "Không thể ghi vào kho lưu ảnh sản phẩm.",
                    ex,
                    CreateData(startedAt));
            }
        }

        private static IReadOnlyDictionary<string, object> CreateData(long startedAt)
            => new Dictionary<string, object>
            {
                ["durationMs"] = Math.Round(
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                    2)
            };
    }
}
