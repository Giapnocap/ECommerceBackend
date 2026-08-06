using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Storage;
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
    public async Task ProductImageStorage_ReportsWritableStorageAndConvertsFailureToUnhealthy()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"ECommerceBackend.StorageHealth.{Guid.NewGuid():N}");

        try
        {
            var storage = new LocalProductImageStorage(
                new TestWebHostEnvironment(root),
                NullLogger<LocalProductImageStorage>.Instance);
            var check = new ProductImageStorageHealthCheck(
                storage,
                NullLogger<ProductImageStorageHealthCheck>.Instance);

            var healthy = await check.CheckHealthAsync(new HealthCheckContext());
            var productsPath = Path.Combine(root, "Uploads", "products");

            Assert.Equal(HealthStatus.Healthy, healthy.Status);
            Assert.Contains("durationMs", healthy.Data.Keys);
            Assert.Empty(Directory.EnumerateFiles(productsPath));

            var failingCheck = new ProductImageStorageHealthCheck(
                new FailingProductImageStorageHealthProbe(),
                NullLogger<ProductImageStorageHealthCheck>.Instance);
            var unavailable = await failingCheck.CheckHealthAsync(
                new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, unavailable.Status);
            Assert.IsType<IOException>(unavailable.Exception);
            Assert.Contains("durationMs", unavailable.Data.Keys);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CustomChecks_PropagateRequestCancellation()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(now);
        IHealthCheck[] checks =
        [
            new DatabaseHealthCheck(
                context,
                NullLogger<DatabaseHealthCheck>.Instance),
            new ProductImageStorageHealthCheck(
                new FailingProductImageStorageHealthProbe(),
                NullLogger<ProductImageStorageHealthCheck>.Instance),
            CreateOutboxCheck(context, timeProvider, enabled: true),
            CreateOrderExpirationCheck(
                context,
                timeProvider,
                new OrderLifecycleOptions { ExpirationEnabled = true }),
            CreateDataRetentionCheck(
                timeProvider,
                new DataRetentionOptions())
        ];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        foreach (var check in checks)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                check.CheckHealthAsync(
                    new HealthCheckContext(),
                    cancellation.Token));
        }
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
        var requiredButDisabledResult = await requiredButDisabled.CheckHealthAsync(
            new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, requiredButDisabledResult.Status);
        Assert.Equal(
            "Xử lý đơn hết hạn là bắt buộc nhưng tiến trình nền đang tắt.",
            requiredButDisabledResult.Description);
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

        var dryRunResult = await dryRun.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, dryRunResult.Status);
        Assert.Equal(
            "Chế độ chạy thử phát hiện đơn hàng đã quá hạn xử lý.",
            dryRunResult.Description);
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
        Assert.Equal(
            "Tiến trình xử lý đơn hết hạn chưa có tín hiệu hoạt động hợp lệ.",
            result.Description);
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
    public async Task Outbox_EmitsBacklogMeasurementsForScaleDecisions()
    {
        var measurements = new ConcurrentQueue<(string Name, double Value)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == "ECommerceBackend.Outbox"
                && instrument.Name.StartsWith("outbox.backlog.", StringComparison.Ordinal))
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) =>
            measurements.Enqueue((instrument.Name, value)));
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            measurements.Enqueue((instrument.Name, value)));
        listener.Start();

        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);
        context.OutboxMessages.AddRange(
            CreatePendingOutboxMessage(now.UtcDateTime.AddSeconds(-137)),
            CreatePendingOutboxMessage(now.UtcDateTime.AddSeconds(-30)),
            CreatePendingOutboxMessage(now.UtcDateTime.AddSeconds(-10)),
            CreateDeadLetterOutboxMessage(now.UtcDateTime),
            CreateDeadLetterOutboxMessage(now.UtcDateTime));
        await context.SaveChangesAsync();

        var result = await CreateOutboxCheck(
                context,
                new FixedTimeProvider(now),
                enabled: true)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains(measurements, item =>
            item.Name == "outbox.backlog.pending" && item.Value == 3);
        Assert.Contains(measurements, item =>
            item.Name == "outbox.backlog.dead_lettered" && item.Value == 2);
        Assert.Contains(measurements, item =>
            item.Name == "outbox.backlog.oldest_age" && item.Value == 137);
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
        var disabledResult = await disabled.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, disabledResult.Status);
        Assert.Equal(
            "Xử lý hàng đợi thông báo là bắt buộc nhưng tiến trình nền đang tắt.",
            disabledResult.Description);

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
        Assert.Equal(
            "Tiến trình gửi thông báo chưa có tín hiệu hoạt động hợp lệ.",
            result.Description);
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
        var requiredButDisabledResult = await requiredButDisabled.CheckHealthAsync(
            new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, requiredButDisabledResult.Status);
        Assert.Equal(
            "Xử lý lưu giữ dữ liệu tự động là bắt buộc nhưng tiến trình nền đang tắt.",
            requiredButDisabledResult.Description);

        var enabledWithoutHeartbeat = CreateDataRetentionCheck(
            timeProvider,
            new DataRetentionOptions
            {
                Enabled = true,
                AutomaticProcessingEnabled = true,
                ProcessingIntervalMinutes = 60
            });
        var enabledWithoutHeartbeatResult = await enabledWithoutHeartbeat.CheckHealthAsync(
            new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, enabledWithoutHeartbeatResult.Status);
        Assert.Equal(
            "Tiến trình lưu giữ dữ liệu chưa có tín hiệu hoạt động hợp lệ.",
            enabledWithoutHeartbeatResult.Description);

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

    private static OutboxMessage CreatePendingOutboxMessage(DateTime occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = "test",
            Payload = "{}",
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt
        };

    private static OutboxMessage CreateDeadLetterOutboxMessage(DateTime occurredAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = "test",
            Payload = "{}",
            OccurredAt = occurredAt,
            NextAttemptAt = occurredAt,
            DeadLetteredAt = occurredAt
        };

    private static DataRetentionHealthCheck CreateDataRetentionCheck(
        TimeProvider timeProvider,
        DataRetentionOptions options,
        DataRetentionWorkerStatus? workerStatus = null)
        => new(
            Options.Create(options),
            timeProvider,
            workerStatus ?? new DataRetentionWorkerStatus());

    private sealed class FailingProductImageStorageHealthProbe :
        IProductImageStorageHealthProbe
    {
        public Task CheckAvailabilityAsync(
            CancellationToken cancellationToken = default)
            => Task.FromException(
                new IOException("Product image storage is unavailable."));
    }
}
