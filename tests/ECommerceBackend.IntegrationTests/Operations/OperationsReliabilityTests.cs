using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class OperationsReliabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAuditEvents_AppliesTrimmedFiltersAndHalfOpenTimeRange()
    {
        await using var context = TestAppDbContext.Create();
        var actorUserId = Guid.NewGuid();
        var matching = Audit(actorUserId, "user.role.assign", "User", Now.UtcDateTime.AddMinutes(-1));
        context.AuditEvents.AddRange(
            matching,
            Audit(actorUserId, "user.role.assign", "User", Now.UtcDateTime),
            Audit(Guid.NewGuid(), "user.role.assign", "User", Now.UtcDateTime.AddMinutes(-1)),
            Audit(actorUserId, "user.password.change", "User", Now.UtcDateTime.AddMinutes(-1)));
        await context.SaveChangesAsync();
        var service = CreateOperationsService(context, actorUserId);

        var result = await service.GetAuditEventsAsync(new AuditQueryParams
        {
            ActorUserId = actorUserId,
            Action = "  user.role.assign  ",
            EntityType = " User ",
            From = Now.UtcDateTime.AddMinutes(-2),
            To = Now.UtcDateTime,
            Page = 1,
            PageSize = 10
        });

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(matching.Id, Assert.Single(result.Items).Id);
    }

    [Fact]
    public async Task AuditManagement_FiltersByEntityIdAndRedactsSensitiveMetadata()
    {
        await using var context = TestAppDbContext.Create();
        var actorUserId = Guid.NewGuid();
        var matching = Audit(actorUserId, "user.update", "User", Now.UtcDateTime.AddMinutes(-1));
        matching.EntityId = Guid.NewGuid().ToString();
        matching.MetadataJson = """
            {"reason":"manual review","password":"secret-password","nested":{"refreshToken":"secret-refresh-token"}}
            """;
        var differentResource = Audit(actorUserId, "user.update", "User", Now.UtcDateTime.AddMinutes(-1));
        differentResource.EntityId = Guid.NewGuid().ToString();
        context.AuditEvents.AddRange(matching, differentResource);
        await context.SaveChangesAsync();
        var service = CreateOperationsService(context, actorUserId);

        var page = await service.GetAuditEventsAsync(new AuditQueryParams
        {
            EntityId = $"  {matching.EntityId}  ",
            Page = 1,
            PageSize = 10
        });
        var detail = await service.GetAuditEventAsync(matching.Id);

        var listedAuditEvent = Assert.Single(page.Items);
        Assert.Equal(matching.Id, listedAuditEvent.Id);
        Assert.Equal(detail.Id, listedAuditEvent.Id);
        Assert.Contains("manual review", detail.MetadataJson, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", detail.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-password", detail.MetadataJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-refresh-token", detail.MetadataJson, StringComparison.Ordinal);
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.GetAuditEventAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AuditWriter_RedactsSensitiveMetadataBeforePersistence()
    {
        await using var context = TestAppDbContext.Create();
        var actorUserId = Guid.NewGuid();
        var writer = CreateAuditWriter(context, actorUserId);

        writer.Write(
            "user.security.update",
            "User",
            Guid.NewGuid().ToString(),
            metadata: new Dictionary<string, object?>
            {
                ["passwordHash"] = "raw-password-hash",
                ["details"] = new Dictionary<string, object?>
                {
                    ["apiKey"] = "raw-api-key"
                },
                ["reason"] = "security review"
            });
        await context.SaveChangesAsync();

        var metadata = (await context.AuditEvents.SingleAsync()).MetadataJson;
        Assert.Contains("security review", metadata, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-password-hash", metadata, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-key", metadata, StringComparison.Ordinal);
    }

    private static AuditWriter CreateAuditWriter(AppDbContext context, Guid actorUserId)
    {
        return new AuditWriter(
            new AuditRepository(context),
            new TestRequestContext(
                actorUserId,
                "test-correlation-id",
                "203.0.113.10"),
            new FixedTimeProvider(Now));
    }

    private static OperationsService CreateOperationsService(
        AppDbContext context,
        Guid actorUserId)
    {
        return new OperationsService(
            new AuditQueryUseCase(new AuditRepository(context)));
    }

    private static AuditEvent Audit(
        Guid actorUserId,
        string action,
        string entityType,
        DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CreatedAt = createdAt
        };
}
