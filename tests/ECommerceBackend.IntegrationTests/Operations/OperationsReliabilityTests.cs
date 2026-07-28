using System.Security.Claims;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
    public async Task RedriveDeadLetter_IsIdempotentAndWritesAuditWithoutExposingPayload()
    {
        await using var context = TestAppDbContext.Create();
        var actorUserId = Guid.NewGuid();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "notification.requested",
            Payload = "{\"secret\":\"must-not-be-returned\"}",
            OccurredAt = Now.UtcDateTime.AddMinutes(-10),
            NextAttemptAt = Now.UtcDateTime.AddMinutes(-1),
            Attempts = 5,
            LastAttemptAt = Now.UtcDateTime.AddMinutes(-1),
            DeadLetteredAt = Now.UtcDateTime.AddMinutes(-1),
            LastError = "SMTP unavailable"
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var service = CreateOperationsService(context, actorUserId);

        var listed = await service.GetDeadLettersAsync(new DeadLetterQueryParams());
        var first = await service.RedriveDeadLetterAsync(message.Id, actorUserId);
        var retry = await service.RedriveDeadLetterAsync(message.Id, actorUserId);

        var item = Assert.Single(listed.Items);
        Assert.Equal(message.Id, item.Id);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(item));
        Assert.True(first.ReDriven);
        Assert.False(retry.ReDriven);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.DeadLetteredAt);
        Assert.Null(message.LastError);
        var auditEvent = Assert.Single(await context.AuditEvents.AsNoTracking().ToListAsync());
        Assert.Equal("outbox.dead_letter.redrive", auditEvent.Action);
        Assert.Equal(actorUserId, auditEvent.ActorUserId);
        Assert.Contains("previousAttempts", auditEvent.MetadataJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("SMTP unavailable", auditEvent.MetadataJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UploadReconciliation_DryRunThenDeletesOnlyEligibleGeneratedOrphan()
    {
        var root = Path.Combine(Path.GetTempPath(), $"ECommerceBackend.Reconcile.{Guid.NewGuid():N}");
        var productsFolder = Path.Combine(root, "Uploads", "products");
        Directory.CreateDirectory(productsFolder);

        try
        {
            await using var context = TestAppDbContext.Create();
            var actorUserId = Guid.NewGuid();
            var referencedName = $"{Guid.NewGuid():N}.png";
            var missingName = $"{Guid.NewGuid():N}.png";
            var oldOrphanName = $"{Guid.NewGuid():N}.png";
            var secondOldOrphanName = $"{Guid.NewGuid():N}.png";
            var freshOrphanName = $"{Guid.NewGuid():N}.png";
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, referencedName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, oldOrphanName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, secondOldOrphanName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, freshOrphanName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, "manual-note.txt"), [1]);
            File.SetLastWriteTimeUtc(
                Path.Combine(productsFolder, oldOrphanName),
                Now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(
                Path.Combine(productsFolder, secondOldOrphanName),
                Now.UtcDateTime.AddHours(-2));
            File.SetLastWriteTimeUtc(
                Path.Combine(productsFolder, freshOrphanName),
                Now.UtcDateTime.AddMinutes(-10));

            context.ProductImages.AddRange(
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ImageUrl = $"/uploads/products/{referencedName}"
                },
                new ProductImage
                {
                    Id = Guid.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ImageUrl = $"/uploads/products/{missingName}"
                });
            await context.SaveChangesAsync();
            var service = new UploadReconciliationService(
                new ProductRepository(context),
                context,
                new TestWebHostEnvironment(root),
                CreateAuditWriter(context, actorUserId),
                Options.Create(new UploadOptions
                {
                    ReconciliationGraceMinutes = 60,
                    MaxReconciliationDeletes = 10
                }),
                new FixedTimeProvider(Now),
                NullLogger<UploadReconciliationService>.Instance);

            var dryRun = await service.ReconcileAsync(
                new UploadReconciliationRequest { DeleteOrphans = false, MaxDeletes = 10 },
                actorUserId);
            Assert.True(dryRun.DryRun);
            Assert.Equal(0, dryRun.DeletedFileCount);
            Assert.Equal(2, dryRun.EligibleOrphanCount);
            Assert.True(File.Exists(Path.Combine(productsFolder, oldOrphanName)));

            var cleanup = await service.ReconcileAsync(
                new UploadReconciliationRequest { DeleteOrphans = true, MaxDeletes = 1 },
                actorUserId);

            Assert.False(cleanup.DryRun);
            Assert.Equal(1, cleanup.MissingFileCount);
            Assert.Equal(2, cleanup.EligibleOrphanCount);
            Assert.Equal(1, cleanup.DeletedFileCount);
            Assert.NotEqual(
                File.Exists(Path.Combine(productsFolder, oldOrphanName)),
                File.Exists(Path.Combine(productsFolder, secondOldOrphanName)));
            Assert.True(File.Exists(Path.Combine(productsFolder, freshOrphanName)));
            Assert.True(File.Exists(Path.Combine(productsFolder, "manual-note.txt")));
            Assert.Single(await context.AuditEvents.AsNoTracking()
                .Where(item => item.Action == "uploads.orphans.delete")
                .ToListAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DataRetention_PreviewsThenAppliesOnlySafeRecords()
    {
        var measurements = new List<RetentionMetricMeasurement>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "ECommerceBackend.Operations"
                && instrument.Name.StartsWith("data_retention.", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add(new RetentionMetricMeasurement(
                instrument.Name,
                measurement,
                FindTag(tags, "mode"),
                FindTag(tags, "result"),
                FindTag(tags, "record.type"))));
        meterListener.Start();

        await using var context = TestAppDbContext.Create();
        var actorUserId = Guid.NewGuid();
        var old = Now.UtcDateTime.AddDays(-31);
        var recent = Now.UtcDateTime.AddDays(-29);
        var processedOutbox = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "notification.requested",
            Payload = "{}",
            OccurredAt = old,
            NextAttemptAt = old,
            ProcessedAt = old
        };
        var deadLetter = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = "notification.requested",
            Payload = "{}",
            OccurredAt = old,
            NextAttemptAt = old,
            DeadLetteredAt = old
        };
        var activeBoundaryToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            TokenHash = "recent-token",
            CreatedAt = old,
            ExpiresAt = recent
        };
        var expiredToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            FamilyId = Guid.NewGuid(),
            TokenHash = "expired-token",
            CreatedAt = old,
            ExpiresAt = old
        };
        var oldWebhook = new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            Provider = "generic-hmac",
            ProviderEventId = "old-event",
            PayloadHash = new string('a', 64),
            Payload = "{\"sensitive\":true}",
            ReceivedAt = old,
            OccurredAt = old,
            ProcessedAt = old
        };
        var recentWebhook = new PaymentWebhookEvent
        {
            Id = Guid.NewGuid(),
            PaymentId = Guid.NewGuid(),
            Provider = "generic-hmac",
            ProviderEventId = "recent-event",
            PayloadHash = new string('b', 64),
            Payload = "{\"recent\":true}",
            ReceivedAt = recent,
            OccurredAt = recent,
            ProcessedAt = recent
        };
        context.OutboxMessages.AddRange(processedOutbox, deadLetter);
        context.RefreshTokens.AddRange(activeBoundaryToken, expiredToken);
        context.PaymentWebhookEvents.AddRange(oldWebhook, recentWebhook);
        await context.SaveChangesAsync();

        var service = CreateOperationsService(context, actorUserId);

        var preview = await service.RunDataRetentionAsync(
            new DataRetentionRequest { MaxBatchSize = 10 }, actorUserId);

        Assert.True(preview.DryRun);
        Assert.Equal(1, preview.ProcessedOutboxCandidateCount);
        Assert.Equal(1, preview.ExpiredRefreshTokenCandidateCount);
        Assert.Equal(1, preview.WebhookPayloadCandidateCount);
        Assert.NotEmpty(oldWebhook.Payload);
        Assert.Equal(2, await context.OutboxMessages.CountAsync());

        var applied = await service.RunDataRetentionAsync(
            new DataRetentionRequest { ApplyChanges = true, MaxBatchSize = 10 }, actorUserId);

        Assert.False(applied.DryRun);
        Assert.Equal(1, applied.ProcessedOutboxDeletedCount);
        Assert.Equal(1, applied.ExpiredRefreshTokenDeletedCount);
        Assert.Equal(1, applied.WebhookPayloadRedactedCount);
        Assert.Null(await context.OutboxMessages.FindAsync(processedOutbox.Id));
        Assert.NotNull(await context.OutboxMessages.FindAsync(deadLetter.Id));
        Assert.Null(await context.RefreshTokens.FindAsync(expiredToken.Id));
        Assert.NotNull(await context.RefreshTokens.FindAsync(activeBoundaryToken.Id));
        Assert.Equal(string.Empty, (await context.PaymentWebhookEvents.FindAsync(oldWebhook.Id))!.Payload);
        Assert.NotEmpty((await context.PaymentWebhookEvents.FindAsync(recentWebhook.Id))!.Payload);
        var auditEvents = await context.AuditEvents
            .Where(item => item.Action == "operations.data_retention.apply")
            .ToListAsync();
        Assert.Single(auditEvents);

        var noOp = await service.RunDataRetentionAsync(
            new DataRetentionRequest { ApplyChanges = true, MaxBatchSize = 10 }, actorUserId);
        Assert.Equal(0, noOp.ProcessedOutboxDeletedCount);
        Assert.Equal(0, noOp.ExpiredRefreshTokenDeletedCount);
        Assert.Equal(0, noOp.WebhookPayloadRedactedCount);
        Assert.Single(await context.AuditEvents
            .Where(item => item.Action == "operations.data_retention.apply")
            .ToListAsync());
        Assert.Contains(measurements, item =>
            item.Name == "data_retention.runs"
            && item.Mode == "preview"
            && item.Result == "success");
        Assert.Contains(measurements, item =>
            item.Name == "data_retention.runs"
            && item.Mode == "apply"
            && item.Result == "success");
        Assert.Contains(measurements, item =>
            item.Name == "data_retention.records.changed"
            && item.RecordType == "processed_outbox"
            && item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "data_retention.records.changed"
            && item.RecordType == "expired_refresh_token"
            && item.Value == 1);
        Assert.Contains(measurements, item =>
            item.Name == "data_retention.records.changed"
            && item.RecordType == "webhook_payload"
            && item.Value == 1);
    }

    private static AuditWriter CreateAuditWriter(AppDbContext context, Guid actorUserId)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "test-correlation-id"
        };
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())
        ], "Test"));
        return new AuditWriter(
            new AuditRepository(context),
            new HttpContextAccessor { HttpContext = httpContext },
            new FixedTimeProvider(Now));
    }

    private static OperationsService CreateOperationsService(
        AppDbContext context,
        Guid actorUserId)
    {
        var consistency = new EfDataConsistencyService(context);
        var audit = CreateAuditWriter(context, actorUserId);
        var clock = new FixedTimeProvider(Now);
        return new OperationsService(
            new DeadLetterUseCase(
                new OutboxRepository(context),
                context,
                consistency,
                audit,
                clock),
            new AuditQueryUseCase(new AuditRepository(context)),
            new DataRetentionUseCase(
                new DataRetentionRepository(context),
                context,
                consistency,
                audit,
                clock,
                RetentionOptions(),
                NullLogger<DataRetentionUseCase>.Instance));
    }

    private static IOptions<DataRetentionOptions> RetentionOptions()
        => Options.Create(new DataRetentionOptions
        {
            Enabled = true,
            ProcessedOutboxRetentionDays = 30,
            ExpiredRefreshTokenRetentionDays = 30,
            WebhookPayloadRetentionDays = 30,
            MaxBatchSize = 100
        });

    private static string? FindTag(
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        string name)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == name)
                return tag.Value?.ToString();
        }

        return null;
    }

    private sealed record RetentionMetricMeasurement(
        string Name,
        long Value,
        string? Mode,
        string? Result,
        string? RecordType);

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
