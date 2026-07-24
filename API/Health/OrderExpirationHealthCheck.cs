using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Orders;
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
        private readonly OrderExpirationWorkerStatus _workerStatus;

        public OrderExpirationHealthCheck(
            AppDbContext context,
            IOptions<OrderLifecycleOptions> options,
            TimeProvider timeProvider,
            ILogger<OrderExpirationHealthCheck> logger,
            OrderExpirationWorkerStatus workerStatus)
        {
            _context = context;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _workerStatus = workerStatus;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_options.ExpirationEnabled && _options.RequireExpirationProcessing)
            {
                return HealthCheckResult.Unhealthy(
                    "Xử lý đơn hết hạn là bắt buộc nhưng worker đang tắt.",
                    data: new Dictionary<string, object>
                    {
                        ["enabled"] = false,
                        ["required"] = true
                    });
            }

            if (!_options.ExpirationEnabled)
                return HealthCheckResult.Healthy("Tiến trình xử lý đơn hàng hết hạn đang tắt.");

            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var worker = _workerStatus.GetSnapshot();
                var heartbeatAgeLimit = TimeSpan.FromSeconds(
                    Math.Max(30, _options.ExpirationPollIntervalSeconds * 3));
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
                if (worker.StartedAt.HasValue)
                    data["workerStartedAt"] = worker.StartedAt.Value;
                if (worker.LastSuccessfulCycleAt.HasValue)
                    data["lastSuccessfulCycleAt"] = worker.LastSuccessfulCycleAt.Value;
                if (worker.LastFailureAt.HasValue)
                    data["lastFailureAt"] = worker.LastFailureAt.Value;

                if (_options.RequireExpirationProcessing
                    && (!worker.LastSuccessfulCycleAt.HasValue
                        || now - worker.LastSuccessfulCycleAt.Value > heartbeatAgeLimit))
                {
                    return HealthCheckResult.Unhealthy(
                        "Worker xử lý đơn hết hạn chưa có heartbeat hợp lệ.",
                        data: data);
                }

                if (_options.ExpirationDryRun && overdueCount > 0)
                    return HealthCheckResult.Degraded("Expiration dry-run found overdue orders.", data: data);

                if (overdueCount > 0)
                    return HealthCheckResult.Unhealthy("Có đơn hàng chờ xử lý quá thời hạn cho phép.", data: data);

                return HealthCheckResult.Healthy("Xử lý đơn hàng hết hạn đang hoạt động bình thường.", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order expiration health check failed.");
                return HealthCheckResult.Unhealthy("Kiểm tra tiến trình xử lý đơn hàng hết hạn thất bại.", ex);
            }
        }
    }
}
