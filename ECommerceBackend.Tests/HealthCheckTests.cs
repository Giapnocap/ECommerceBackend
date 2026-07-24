using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Orders;
using ECommerceBackend.Infrastructure.Maintenance;
using ECommerceBackend.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class HealthCheckTests
{
    [Fact]
    public async Task Database_ReportsHealthyAndConvertsProviderFailureToUnhealthy()
    {
        var context = TestAppDbContext.Create();
        var check = new DatabaseHealthCheck(
            context,
            NullLogger<DatabaseHealthCheck>.Instance);

        var healthy = await check.CheckHealthAsync(new HealthCheckContext());
        await context.DisposeAsync();
        var unavailable = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, healthy.Status);
        Assert.Contains("durationMs", healthy.Data.Keys);
        Assert.Contains("provider", healthy.Data.Keys);
        Assert.Equal(HealthStatus.Unhealthy, unavailable.Status);
        Assert.NotNull(unavailable.Exception);
    }

    [Fact]
    public async Task OrderExpiration_ReportsDisabledHealthyDryRunDegradedAndLiveUnhealthy()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        var disabled = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions { ExpirationEnabled = false });
        Assert.Equal(
            HealthStatus.Healthy,
            (await disabled.CheckHealthAsync(new HealthCheckContext())).Status);

        var requiredButDisabled = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions
            {
                RequireExpirationProcessing = true,
                ExpirationEnabled = false
            });
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await requiredButDisabled.CheckHealthAsync(new HealthCheckContext())).Status);
        var enabledWithoutOverdueOrders = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions
            {
                ExpirationEnabled = true,
                ExpirationDryRun = false,
                MaxOverdueMinutes = 15
            });
        Assert.Equal(
            HealthStatus.Healthy,
            (await enabledWithoutOverdueOrders.CheckHealthAsync(new HealthCheckContext())).Status);

        var overdueOrder = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrderNumber = "OVERDUE-ORDER",
            IdempotencyKey = "overdue-key",
            IdempotencyRequestHash = new string('A', 64),
            OrderDate = now.UtcDateTime.AddHours(-2),
            ShippingAddress = "Health check"
        };
        overdueOrder.SetPendingExpiration(now.UtcDateTime.AddHours(-1));
        context.Orders.Add(overdueOrder);
        await context.SaveChangesAsync();

        var dryRun = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions
            {
                ExpirationEnabled = true,
                ExpirationDryRun = true,
                MaxOverdueMinutes = 15
            });
        var live = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions
            {
                ExpirationEnabled = true,
                ExpirationDryRun = false,
                MaxOverdueMinutes = 15
            });

        Assert.Equal(
            HealthStatus.Degraded,
            (await dryRun.CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await live.CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task OrderExpiration_ReportsMissingWorkerHeartbeatWhenProcessingIsRequired()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        var workerStatus = new OrderExpirationWorkerStatus();
        workerStatus.MarkStarted(now.UtcDateTime.AddMinutes(-2));
        workerStatus.MarkSuccessfulCycle(now.UtcDateTime.AddMinutes(-2));

        var check = CreateOrderExpirationCheck(
            context,
            timeProvider,
            new OrderLifecycleOptions
            {
                RequireExpirationProcessing = true,
                ExpirationEnabled = true,
                ExpirationDryRun = false,
                ExpirationPollIntervalSeconds = 30
            },
            workerStatus);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(now.UtcDateTime.AddMinutes(-2), result.Data["lastSuccessfulCycleAt"]);
    }

    [Fact]
    public async Task Outbox_ReportsHealthyDeadLetterAndStaleBacklogStates()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        Assert.Equal(
            HealthStatus.Healthy,
            (await CreateOutboxCheck(context, timeProvider, enabled: false)
                .CheckHealthAsync(new HealthCheckContext())).Status);
        Assert.Equal(
            HealthStatus.Healthy,
            (await CreateOutboxCheck(context, timeProvider, enabled: true)
                .CheckHealthAsync(new HealthCheckContext())).Status);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "test",
            Payload = "{}",
            OccurredAt = now.UtcDateTime,
            NextAttemptAt = now.UtcDateTime,
            DeadLetteredAt = now.UtcDateTime
        });
        await context.SaveChangesAsync();
        Assert.Equal(
            HealthStatus.Degraded,
            (await CreateOutboxCheck(context, timeProvider, enabled: true)
                .CheckHealthAsync(new HealthCheckContext())).Status);

        context.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "test",
            Payload = "{}",
            OccurredAt = now.UtcDateTime.AddMinutes(-60),
            NextAttemptAt = now.UtcDateTime
        });
        await context.SaveChangesAsync();
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await CreateOutboxCheck(context, timeProvider, enabled: true)
                .CheckHealthAsync(new HealthCheckContext())).Status);
    }

    [Fact]
    public async Task Outbox_RequiredProcessingReportsDisabledAndStaleWorker()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        var disabled = CreateOutboxCheck(
            context,
            timeProvider,
            enabled: false,
            requireProcessing: true);
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await disabled.CheckHealthAsync(new HealthCheckContext())).Status);

        var workerStatus = new OutboxWorkerStatus();
        workerStatus.MarkStarted(now.UtcDateTime.AddMinutes(-2));
        workerStatus.MarkSuccessfulCycle(now.UtcDateTime.AddMinutes(-2));
        var stale = CreateOutboxCheck(
            context,
            timeProvider,
            enabled: true,
            requireProcessing: true,
            workerStatus: workerStatus);

        var result = await stale.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(now.UtcDateTime.AddMinutes(-2), result.Data["lastSuccessfulCycleAt"]);
    }

    [Fact]
    public async Task DataRetention_ReportsDisabledMissingAndHealthyWorkerStates()
    {
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);

        var disabled = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions());
        Assert.Equal(
            HealthStatus.Healthy,
            (await disabled.CheckHealthAsync(new HealthCheckContext())).Status);

        var requiredButDisabled = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions { RequireAutomaticProcessing = true });
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await requiredButDisabled.CheckHealthAsync(new HealthCheckContext())).Status);

        var enabledWithoutHeartbeat = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                ProcessingIntervalMinutes = 60
            });
        Assert.Equal(
            HealthStatus.Degraded,
            (await enabledWithoutHeartbeat.CheckHealthAsync(new HealthCheckContext())).Status);

        var requiredWithoutHeartbeat = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                RequireAutomaticProcessing = true,
                ProcessingIntervalMinutes = 60
            });
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await requiredWithoutHeartbeat.CheckHealthAsync(new HealthCheckContext())).Status);

        var status = new DataRetentionWorkerStatus();
        status.MarkStarted(now.UtcDateTime.AddMinutes(-1));
        status.MarkSuccessfulCycle(now.UtcDateTime, 12);
        var healthy = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                RequireAutomaticProcessing = true,
                ProcessingIntervalMinutes = 60
            },
            status);
        var healthyResult = await healthy.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, healthyResult.Status);
        Assert.Equal(12L, healthyResult.Data["lastChangedRecordCount"]);

        status.MarkFailure(now.UtcDateTime.AddSeconds(1));
        var failedResult = await healthy.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, failedResult.Status);
    }

    private static OrderExpirationHealthCheck CreateOrderExpirationCheck(
        AppDbContext context,
        TimeProvider timeProvider,
        OrderLifecycleOptions options,
        OrderExpirationWorkerStatus? workerStatus = null)
    {
        var hasWorkerStatus = workerStatus != null;
        workerStatus ??= new OrderExpirationWorkerStatus();
        if (options.ExpirationEnabled && !hasWorkerStatus)
        {
            workerStatus.MarkStarted(timeProvider.GetUtcNow().UtcDateTime);
            workerStatus.MarkSuccessfulCycle(timeProvider.GetUtcNow().UtcDateTime);
        }

        return new OrderExpirationHealthCheck(
            context,
            Options.Create(options),
            timeProvider,
            NullLogger<OrderExpirationHealthCheck>.Instance,
            workerStatus);
    }

    private static OutboxHealthCheck CreateOutboxCheck(
        AppDbContext context,
        TimeProvider timeProvider,
        bool enabled,
        bool requireProcessing = false,
        OutboxWorkerStatus? workerStatus = null)
        => new(
            context,
            Options.Create(new OutboxOptions
            {
                Enabled = enabled,
                RequireProcessing = requireProcessing,
                PollIntervalSeconds = 30,
                MaxPendingAgeMinutes = 15
            }),
            NullLogger<OutboxHealthCheck>.Instance,
            timeProvider,
            workerStatus ?? new OutboxWorkerStatus());

    private static DataRetentionHealthCheck CreateDataRetentionCheck(
        TimeProvider timeProvider,
        DataRetentionOptions options,
        DataRetentionWorkerStatus? workerStatus = null)
        => new(
            Options.Create(options),
            timeProvider,
            workerStatus ?? new DataRetentionWorkerStatus());
}
