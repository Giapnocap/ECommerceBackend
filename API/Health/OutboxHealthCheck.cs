using ECommerceBackend.Application.Common;
using ECommerceBackend.Infrastructure.Data;
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

        public OutboxHealthCheck(
            AppDbContext context,
            IOptions<OutboxOptions> options,
            ILogger<OutboxHealthCheck> logger)
            : this(context, options, logger, TimeProvider.System)
        {
        }

        public OutboxHealthCheck(
            AppDbContext context,
            IOptions<OutboxOptions> options,
            ILogger<OutboxHealthCheck> logger,
            TimeProvider timeProvider)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled)
                return HealthCheckResult.Healthy("Outbox dispatcher is disabled.");

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

                var oldestPendingAgeMinutes = oldestPendingAt.HasValue
                    ? Math.Max(0, (UtcNow - oldestPendingAt.Value).TotalMinutes)
                    : 0;
                var data = new Dictionary<string, object>
                {
                    ["pendingCount"] = pendingCount,
                    ["deadLetterCount"] = deadLetterCount,
                    ["oldestPendingAgeMinutes"] = Math.Round(oldestPendingAgeMinutes, 2)
                };

                if (oldestPendingAt.HasValue
                    && oldestPendingAgeMinutes > _options.MaxPendingAgeMinutes)
                {
                    return HealthCheckResult.Unhealthy(
                        "Outbox backlog is older than the configured threshold.",
                        data: data);
                }

                if (deadLetterCount > 0)
                {
                    return HealthCheckResult.Degraded(
                        "Outbox contains dead-lettered messages.",
                        data: data);
                }

                return HealthCheckResult.Healthy("Outbox is operating normally.", data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox health check failed.");
                return HealthCheckResult.Unhealthy("Outbox health check failed.", ex);
            }
        }
    }
}
