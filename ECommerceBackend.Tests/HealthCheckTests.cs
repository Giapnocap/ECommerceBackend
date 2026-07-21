using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class HealthCheckTests
{
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

    private static OrderExpirationHealthCheck CreateOrderExpirationCheck(
        AppDbContext context,
        TimeProvider timeProvider,
        OrderLifecycleOptions options)
        => new(
            context,
            Options.Create(options),
            timeProvider,
            NullLogger<OrderExpirationHealthCheck>.Instance);

    private static OutboxHealthCheck CreateOutboxCheck(
        AppDbContext context,
        TimeProvider timeProvider,
        bool enabled)
        => new(
            context,
            Options.Create(new OutboxOptions
            {
                Enabled = enabled,
                MaxPendingAgeMinutes = 15
            }),
            NullLogger<OutboxHealthCheck>.Instance,
            timeProvider);
}
