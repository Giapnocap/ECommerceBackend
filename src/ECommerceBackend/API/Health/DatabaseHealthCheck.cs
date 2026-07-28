using System.Diagnostics;
using ECommerceBackend.Infrastructure.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Health
{
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DatabaseHealthCheck> _logger;

        public DatabaseHealthCheck(
            AppDbContext context,
            ILogger<DatabaseHealthCheck> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                if (!canConnect)
                    return HealthCheckResult.Unhealthy("Không thể kết nối cơ sở dữ liệu.");

                return HealthCheckResult.Healthy(
                    "Kết nối cơ sở dữ liệu đang hoạt động.",
                    new Dictionary<string, object>
                    {
                        ["provider"] = _context.Database.ProviderName ?? "unknown",
                        ["durationMs"] = Math.Round(
                            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                            2)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed.");
                return HealthCheckResult.Unhealthy("Kiểm tra kết nối cơ sở dữ liệu thất bại.", ex);
            }
        }
    }
}
