using ECommerceBackend.Application.Common;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.API.Health
{
    public sealed class OutboxHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _context;
        private readonly OutboxOptions _options;
        private readonly ILogger<OutboxHealthCheck> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly OutboxWorkerStatus _workerStatus;

        public OutboxHealthCheck(
            AppDbContext context,
            IOptions<OutboxOptions> options,
            ILogger<OutboxHealthCheck> logger)
            : this(context, options, logger, TimeProvider.System, new OutboxWorkerStatus())
        {
        }

        public OutboxHealthCheck(
            AppDbContext context,
            IOptions<OutboxOptions> options,
            ILogger<OutboxHealthCheck> logger,
            TimeProvider timeProvider,
            OutboxWorkerStatus workerStatus)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
            _workerStatus = workerStatus;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled && _options.RequireProcessing)
            {
                return HealthCheckResult.Unhealthy(
                    "Xử lý hàng đợi thông báo là bắt buộc nhưng worker đang tắt.",
                    data: new Dictionary<string, object>
                    {
                        ["enabled"] = false,
                        ["required"] = true
                    });
            }

            if (!_options.Enabled)
                return HealthCheckResult.Healthy("Tiến trình gửi thông báo đang tắt.");

            try
            {
                var pendingQuery = _context.OutboxMessages
                    .AsNoTracking()
                    .Where(message => message.ProcessedAt == null
                        && message.DeadLetteredAt == null);
                var pendingCount = await pendingQuery.CountAsync(cancellationToken);
                var deadLetterCount = await _context.OutboxMessages
                    .AsNoTracking()
                    .CountAsync(message => message.DeadLetteredAt != null, cancellationToken);
                var oldestPendingAt = await pendingQuery
                    .Select(message => (DateTime?)message.OccurredAt)
                    .MinAsync(cancellationToken);
                var worker = _workerStatus.GetSnapshot();
                var heartbeatAgeLimit = TimeSpan.FromSeconds(
                    Math.Max(30, _options.PollIntervalSeconds * 3));

                var oldestPendingAgeMinutes = oldestPendingAt.HasValue
                    ? Math.Max(0, (UtcNow - oldestPendingAt.Value).TotalMinutes)
                    : 0;
                var data = new Dictionary<string, object>
                {
                    ["pendingCount"] = pendingCount,
                    ["deadLetterCount"] = deadLetterCount,
                    ["oldestPendingAgeMinutes"] = Math.Round(oldestPendingAgeMinutes, 2)
                };
                if (worker.StartedAt.HasValue)
                    data["workerStartedAt"] = worker.StartedAt.Value;
                if (worker.LastSuccessfulCycleAt.HasValue)
                    data["lastSuccessfulCycleAt"] = worker.LastSuccessfulCycleAt.Value;
                if (worker.LastFailureAt.HasValue)
                    data["lastFailureAt"] = worker.LastFailureAt.Value;

                if (_options.RequireProcessing
                    && (!worker.LastSuccessfulCycleAt.HasValue
                        || UtcNow - worker.LastSuccessfulCycleAt.Value > heartbeatAgeLimit))
                {
                    return HealthCheckResult.Unhealthy(
                        "Worker gửi thông báo chưa có heartbeat hợp lệ.",
                        data: data);
                }

                if (oldestPendingAt.HasValue
                    && oldestPendingAgeMinutes > _options.MaxPendingAgeMinutes)
                {
                    return HealthCheckResult.Unhealthy(
                        "Hàng đợi thông báo bị tồn đọng quá thời gian cấu hình.",
                        data: data);
                }

                if (deadLetterCount > 0)
                {
                    return HealthCheckResult.Degraded(
                        "Hàng đợi có thông báo gửi thất bại.",
                        data: data);
                }

                return HealthCheckResult.Healthy("Hàng đợi thông báo đang hoạt động bình thường.", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox health check failed.");
                return HealthCheckResult.Unhealthy("Kiểm tra hàng đợi thông báo thất bại.", ex);
            }
        }
    }
}
