using ECommerceBackend.API.Health;
using ECommerceBackend.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

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
}
