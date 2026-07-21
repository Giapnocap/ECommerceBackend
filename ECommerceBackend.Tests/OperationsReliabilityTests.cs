using System.Security.Claims;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
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
        var audit = CreateAuditWriter(context, actorUserId);
        var service = new OperationsService(
            context,
            new EfDataConsistencyService(context),
            audit,
            new FixedTimeProvider(Now));

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
            var freshOrphanName = $"{Guid.NewGuid():N}.png";
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, referencedName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, oldOrphanName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, freshOrphanName), [1]);
            await File.WriteAllBytesAsync(Path.Combine(productsFolder, "manual-note.txt"), [1]);
            File.SetLastWriteTimeUtc(
                Path.Combine(productsFolder, oldOrphanName),
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
            Assert.True(File.Exists(Path.Combine(productsFolder, oldOrphanName)));

            var cleanup = await service.ReconcileAsync(
                new UploadReconciliationRequest { DeleteOrphans = true, MaxDeletes = 10 },
                actorUserId);

            Assert.False(cleanup.DryRun);
            Assert.Equal(1, cleanup.MissingFileCount);
            Assert.Equal(1, cleanup.DeletedFileCount);
            Assert.False(File.Exists(Path.Combine(productsFolder, oldOrphanName)));
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

    private static AuditWriter CreateAuditWriter(AppDbContext context, Guid actorUserId)
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "phase-3-correlation"
        };
        httpContext.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, actorUserId.ToString())
        ], "Test"));
        return new AuditWriter(
            context,
            new HttpContextAccessor { HttpContext = httpContext },
            new FixedTimeProvider(Now));
    }
}
