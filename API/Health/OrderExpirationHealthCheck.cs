using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.API.Health
{
    public sealed class OrderExpirationHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _context;
        private readonly OrderLifecycleOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<OrderExpirationHealthCheck> _logger;

        public OrderExpirationHealthCheck(
            AppDbContext context,
            IOptions<OrderLifecycleOptions> options,
            TimeProvider timeProvider,
            ILogger<OrderExpirationHealthCheck> logger)
        {
            _context = context;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_options.ExpirationEnabled)
                return HealthCheckResult.Healthy("Order expiration worker is disabled.");

            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var overdueThreshold = now.AddMinutes(-_options.MaxOverdueMinutes);
                var overdueCount = await _context.Orders
                    .AsNoTracking()
                    .CountAsync(order => order.Status == OrderStatus.Pending
                        && order.ExpiresAt != null
                        && order.ExpiresAt < overdueThreshold, cancellationToken);
                var data = new Dictionary<string, object>
                {
                    ["overdueCount"] = overdueCount,
                    ["dryRun"] = _options.ExpirationDryRun
                };

                if (_options.ExpirationDryRun && overdueCount > 0)
                    return HealthCheckResult.Degraded("Expiration dry-run found overdue orders.", data: data);

                if (overdueCount > 0)
                    return HealthCheckResult.Unhealthy("Pending orders exceed the expiration delay threshold.", data: data);

                return HealthCheckResult.Healthy("Order expiration is operating normally.", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order expiration health check failed.");
                return HealthCheckResult.Unhealthy("Order expiration health check failed.", ex);
            }
        }
    }
}
