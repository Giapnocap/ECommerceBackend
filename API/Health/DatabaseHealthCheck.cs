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
            try
            {
                var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
                return canConnect
                    ? HealthCheckResult.Healthy("Database connection is available.")
                    : HealthCheckResult.Unhealthy("Database connection is not available.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed.");
                return HealthCheckResult.Unhealthy("Database connection check failed.", ex);
            }
        }
    }
}