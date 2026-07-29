using System.Security.Cryptography;
using System.Text;
using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Security;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public class PaymentAndOutboxTests
{
    private const string WebhookSecret = "test-payment-webhook-secret-32-bytes-minimum";

    [Fact]
    public async Task PaymentWebhook_IsSignedIdempotentAndWritesNotificationOutbox()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(
            context,
            "generic-hmac",
            "txn-001",
            new DateTime(2026, 7, 17, 9, 0, 0, DateTimeKind.Utc));
        var options = PaymentOptions();
        var receivedAt = new DateTimeOffset(2026, 7, 19, 14, 30, 0, TimeSpan.Zero);
        var provider = new GenericHmacPaymentProvider(options);
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([provider]),
            new OutboxWriter(new OutboxRepository(context)),
            options,
            new FixedTimeProvider(receivedAt));
        const string payload = "{\"providerTransactionId\":\"txn-001\",\"status\":\"paid\",\"amount\":100,\"occurredAt\":\"2026-07-17T10:00:00Z\"}";
        var request = new PaymentWebhookRequest("evt-001", Sign("evt-001", payload), payload);

        var first = await service.HandleAsync("generic-hmac", request);
        var duplicate = await service.HandleAsync("generic-hmac", request);

        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(nameof(PaymentStatus.Paid), first.Status);
        Assert.Equal(PaymentStatus.Paid, (await context.Payments.FindAsync(payment.Id))!.Status);
        var savedEvent = await context.PaymentWebhookEvents.SingleAsync();
        Assert.True(savedEvent.StatusChanged);
        Assert.Equal(PaymentStatus.Paid, savedEvent.ResultingStatus);
        Assert.Equal(DateTime.Parse("2026-07-17T10:00:00Z").ToUniversalTime(), savedEvent.OccurredAt);
        Assert.Equal(receivedAt.UtcDateTime, savedEvent.ReceivedAt);
        Assert.Equal(receivedAt.UtcDateTime, savedEvent.ProcessedAt);
        Assert.Equal(string.Empty, savedEvent.Payload);
        Assert.Equal(2, await context.PaymentStatusHistories.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task PaymentWebhook_ReusedEventIdWithDifferentPayload_IsRejected()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(
            context,
            "generic-hmac",
            "txn-event-reuse");
        var options = PaymentOptions();
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver(
                [new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string paidPayload =
            "{\"providerTransactionId\":\"txn-event-reuse\",\"status\":\"paid\",\"amount\":100}";
        const string refundedPayload =
            "{\"providerTransactionId\":\"txn-event-reuse\",\"status\":\"refunded\",\"amount\":100}";

        _ = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest(
                "evt-reused",
                Sign("evt-reused", paidPayload),
                paidPayload));
        var exception =
            await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(
                () => service.HandleAsync(
                    "generic-hmac",
                    new PaymentWebhookRequest(
                        "evt-reused",
                        Sign("evt-reused", refundedPayload),
                        refundedPayload)));

        Assert.Equal(
            "webhook_event_payload_mismatch",
            exception.Code);
        Assert.Equal(
            PaymentStatus.Paid,
            (await context.Payments.FindAsync(payment.Id))!.Status);
        Assert.Single(await context.PaymentWebhookEvents.ToListAsync());
        Assert.Equal(2, await context.PaymentStatusHistories.CountAsync());
        Assert.Single(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task PaymentWebhook_RetainsRawPayloadOnlyWhenExplicitlyEnabled()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(context, "generic-hmac", "txn-retain-payload");
        var options = PaymentOptions(retainRawPayload: true);
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string payload = "{\"providerTransactionId\":\"txn-retain-payload\",\"status\":\"paid\",\"amount\":100}";

        _ = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest("evt-retain-payload", Sign("evt-retain-payload", payload), payload));

        var savedEvent = await context.PaymentWebhookEvents.SingleAsync();
        Assert.Equal(payload, savedEvent.Payload);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))),
            savedEvent.PayloadHash);
        Assert.Equal(payment.Id, savedEvent.PaymentId);
    }

    [Fact]
    public async Task PaymentWebhook_ReplayReturnsOriginalResultAfterLaterRefund()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(context, "generic-hmac", "txn-replay");
        var options = PaymentOptions();
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string paidPayload = "{\"providerTransactionId\":\"txn-replay\",\"status\":\"paid\",\"amount\":100}";
        const string refundPayload = "{\"providerTransactionId\":\"txn-replay\",\"status\":\"refunded\",\"amount\":100}";
        var paidRequest = new PaymentWebhookRequest("evt-paid", Sign("evt-paid", paidPayload), paidPayload);
        var refundRequest = new PaymentWebhookRequest("evt-refund", Sign("evt-refund", refundPayload), refundPayload);

        _ = await service.HandleAsync("generic-hmac", paidRequest);
        _ = await service.HandleAsync("generic-hmac", refundRequest);
        var replay = await service.HandleAsync("generic-hmac", paidRequest);

        Assert.True(replay.Duplicate);
        Assert.Equal(nameof(PaymentStatus.Paid), replay.Status);
        Assert.Equal(PaymentStatus.Refunded, (await context.Payments.FindAsync(payment.Id))!.Status);
        Assert.Equal(2, await context.PaymentWebhookEvents.CountAsync());
        Assert.Equal(3, await context.PaymentStatusHistories.CountAsync());
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task PaymentWebhook_RefundRetries_DoNotDuplicateOutcome()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(
            context,
            "generic-hmac",
            "txn-refund-retry");
        var options = PaymentOptions();
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver(
                [new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string paidPayload =
            "{\"providerTransactionId\":\"txn-refund-retry\",\"status\":\"paid\",\"amount\":100}";
        const string refundPayload =
            "{\"providerTransactionId\":\"txn-refund-retry\",\"status\":\"refunded\",\"amount\":100}";
        var refundRequest = new PaymentWebhookRequest(
            "evt-refund-first",
            Sign("evt-refund-first", refundPayload),
            refundPayload);

        _ = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest(
                "evt-paid-first",
                Sign("evt-paid-first", paidPayload),
                paidPayload));
        var firstRefund = await service.HandleAsync(
            "generic-hmac",
            refundRequest);
        var exactReplay = await service.HandleAsync(
            "generic-hmac",
            refundRequest);
        var providerRetry = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest(
                "evt-refund-second",
                Sign("evt-refund-second", refundPayload),
                refundPayload));

        Assert.False(firstRefund.Duplicate);
        Assert.True(exactReplay.Duplicate);
        Assert.False(providerRetry.Duplicate);
        Assert.Equal(nameof(PaymentStatus.Refunded), providerRetry.Status);
        Assert.Equal(
            PaymentStatus.Refunded,
            (await context.Payments.FindAsync(payment.Id))!.Status);
        Assert.Equal(3, await context.PaymentWebhookEvents.CountAsync());
        Assert.Equal(3, await context.PaymentStatusHistories.CountAsync());
        Assert.Equal(2, await context.OutboxMessages.CountAsync());
        Assert.False((await context.PaymentWebhookEvents.SingleAsync(
            item => item.ProviderEventId == "evt-refund-second"))
            .StatusChanged);
    }

    [Fact]
    public async Task PaymentWebhook_NewEventWithSameStatus_IsAuditedWithoutDuplicateNotification()
    {
        await using var context = TestAppDbContext.Create();
        var (_, _) = await SeedPaymentAsync(context, "generic-hmac", "txn-same-state");
        var options = PaymentOptions();
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string payload = "{\"providerTransactionId\":\"txn-same-state\",\"status\":\"paid\",\"amount\":100}";

        _ = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest("evt-first", Sign("evt-first", payload), payload));
        var sameState = await service.HandleAsync(
            "generic-hmac",
            new PaymentWebhookRequest("evt-second", Sign("evt-second", payload), payload));

        Assert.False(sameState.Duplicate);
        Assert.Equal(nameof(PaymentStatus.Paid), sameState.Status);
        Assert.Equal(2, await context.PaymentWebhookEvents.CountAsync());
        Assert.False((await context.PaymentWebhookEvents
            .SingleAsync(item => item.ProviderEventId == "evt-second")).StatusChanged);
        Assert.Equal(2, await context.PaymentStatusHistories.CountAsync());
        Assert.Equal(1, await context.OutboxMessages.CountAsync());
    }

    [Fact]
    public void PaymentProviderResolver_RejectsDuplicateProviderCode()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new PaymentProviderResolver(
            [
                new CashOnDeliveryPaymentProvider(),
                new CashOnDeliveryPaymentProvider()
            ]));

        Assert.Contains("registered more than once", exception.Message);
    }

    [Fact]
    public void PaymentProviderResolver_ExposesOnlyCheckoutCapableProviders()
    {
        var resolver = new PaymentProviderResolver(
        [
            new CashOnDeliveryPaymentProvider(),
            new GenericHmacPaymentProvider(PaymentOptions())
        ]);

        var capability = Assert.Single(resolver.GetCheckoutCapabilities());

        Assert.Equal(PaymentMethod.CashOnDelivery, capability.Method);
        Assert.Equal("cod", capability.ProviderCode);
        Assert.False(capability.SupportsWebhooks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad code")]
    [InlineData("bad/code")]
    public void PaymentProviderResolver_RejectsUnsafeProviderCode(string code)
    {
        Assert.Throws<InvalidOperationException>(() => new PaymentProviderResolver(
        [
            new StubPaymentProvider(
                code,
                PaymentMethod.CashOnDelivery,
                supportsWebhooks: false,
                new PaymentInitializationResult(PaymentStatus.Pending, code))
        ]));
    }

    [Fact]
    public void PaymentProviderResolver_RejectsUndefinedCheckoutMethod()
    {
        Assert.Throws<InvalidOperationException>(() => new PaymentProviderResolver(
        [
            new StubPaymentProvider(
                "invalid-method",
                (PaymentMethod)99,
                supportsWebhooks: false,
                new PaymentInitializationResult(PaymentStatus.Pending, "invalid-method"))
        ]));
    }

    [Fact]
    public void PaymentProviderContract_NormalizesValidInitializationAndRejectsBrokenAdapters()
    {
        var valid = new StubPaymentProvider(
            " provider-one ",
            PaymentMethod.CashOnDelivery,
            supportsWebhooks: true,
            new PaymentInitializationResult(PaymentStatus.Pending, "PROVIDER-ONE", " transaction-1 "));

        var normalized = PaymentProviderContract.NormalizeInitialization(
            valid,
            valid.Initialize(new PaymentInitializationRequest(Guid.NewGuid(), "ORDER-1", 100)));

        Assert.Equal("provider-one", normalized.Provider);
        Assert.Equal("transaction-1", normalized.ProviderTransactionId);
        Assert.Throws<InvalidOperationException>(() => PaymentProviderContract.NormalizeInitialization(
            valid,
            new PaymentInitializationResult(PaymentStatus.Pending, "another-provider", "transaction-1")));
        Assert.Throws<InvalidOperationException>(() => PaymentProviderContract.NormalizeInitialization(
            valid,
            new PaymentInitializationResult(PaymentStatus.Refunded, "provider-one", "transaction-1")));
        Assert.Throws<InvalidOperationException>(() => PaymentProviderContract.NormalizeInitialization(
            valid,
            new PaymentInitializationResult(PaymentStatus.Pending, "provider-one", new string('x', 201))));
        Assert.Throws<InvalidOperationException>(() => PaymentProviderContract.NormalizeInitialization(
            valid,
            new PaymentInitializationResult(PaymentStatus.Pending, "provider-one")));
    }

    [Fact]
    public async Task GenericHmacProvider_RejectsInvalidSignature()
    {
        var provider = new GenericHmacPaymentProvider(PaymentOptions());
        const string payload = "{\"providerTransactionId\":\"txn-001\",\"status\":\"paid\"}";

        var exception = await Assert.ThrowsAsync<Application.Exceptions.ApiException>(() =>
            provider.VerifyWebhookAsync(new PaymentWebhookRequest("evt-001", "00", payload)));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("invalid_webhook_signature", exception.Code);
    }

    [Fact]
    public async Task GenericHmacProvider_RequiresAmountForPaidStatus()
    {
        var provider = new GenericHmacPaymentProvider(PaymentOptions());
        const string payload = "{\"providerTransactionId\":\"txn-amount-required\",\"status\":\"paid\"}";

        var exception = await Assert.ThrowsAsync<Application.Exceptions.ApiException>(() =>
            provider.VerifyWebhookAsync(new PaymentWebhookRequest(
                "evt-amount-required",
                Sign("evt-amount-required", payload),
                payload)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_webhook_amount", exception.Code);
    }

    [Fact]
    public async Task PaymentWebhook_RejectsMismatchedAmountWithoutMutation()
    {
        await using var context = TestAppDbContext.Create();
        var (payment, _) = await SeedPaymentAsync(context, "generic-hmac", "txn-amount-mismatch");
        var options = PaymentOptions();
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([new GenericHmacPaymentProvider(options)]),
            new OutboxWriter(new OutboxRepository(context)),
            options);
        const string payload = "{\"providerTransactionId\":\"txn-amount-mismatch\",\"status\":\"paid\",\"amount\":99}";

        var exception = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(() =>
            service.HandleAsync(
                "generic-hmac",
                new PaymentWebhookRequest(
                    "evt-amount-mismatch",
                    Sign("evt-amount-mismatch", payload),
                    payload)));

        Assert.Equal("payment_amount_mismatch", exception.Code);
        Assert.Equal(PaymentStatus.Pending, (await context.Payments.FindAsync(payment.Id))!.Status);
        Assert.Empty(await context.PaymentWebhookEvents.ToListAsync());
        Assert.Single(await context.PaymentStatusHistories.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task PaymentWebhook_RejectsOccurrenceTooFarInFutureWithoutMutation()
    {
        await using var context = TestAppDbContext.Create();
        var receivedAt = new DateTimeOffset(2026, 7, 19, 14, 30, 0, TimeSpan.Zero);
        var createdAt = receivedAt.UtcDateTime.AddMinutes(-1);
        var (payment, _) = await SeedPaymentAsync(
            context,
            "generic-hmac",
            "txn-future",
            createdAt);
        var options = PaymentOptions();
        var clock = new FixedTimeProvider(receivedAt);
        var service = new PaymentWebhookService(
            new PaymentRepository(context),
            context,
            new EfDataConsistencyService(context),
            new PaymentProviderResolver([new GenericHmacPaymentProvider(options, clock)]),
            new OutboxWriter(new OutboxRepository(context)),
            options,
            clock);
        const string payload = "{\"providerTransactionId\":\"txn-future\",\"status\":\"paid\",\"amount\":100,\"occurredAt\":\"2026-07-19T14:36:00Z\"}";

        var exception = await Assert.ThrowsAsync<Application.Exceptions.ApiException>(() =>
            service.HandleAsync(
                "generic-hmac",
                new PaymentWebhookRequest(
                    "evt-future",
                    Sign("evt-future", payload),
                    payload)));

        Assert.Equal("webhook_occurrence_in_future", exception.Code);
        Assert.Equal(PaymentStatus.Pending, (await context.Payments.FindAsync(payment.Id))!.Status);
        Assert.Empty(await context.PaymentWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task OutboxWriter_UsesInjectedClockForScheduling()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
        var writer = new OutboxWriter(
            new OutboxRepository(context),
            new FixedTimeProvider(now));

        writer.EnqueueNotification(Guid.NewGuid(), "Subject", "Message");
        await context.SaveChangesAsync();

        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(now.UtcDateTime, message.OccurredAt);
        Assert.Equal(now.UtcDateTime, message.NextAttemptAt);
    }

    [Fact]
    public async Task SensitiveOutboxPayload_IsProtectedAtRest_AndCanBeDispatched()
    {
        await using var context = TestAppDbContext.Create();
        var (_, user) = await SeedPaymentAsync(
            context,
            "generic-hmac",
            "txn-protected-outbox");
        var protector = new DataProtectionSensitivePayloadProtector(
            new EphemeralDataProtectionProvider());
        const string sensitiveMessage = "reset-token=not-for-database-plaintext";
        var writer = new OutboxWriter(
            new OutboxRepository(context),
            TimeProvider.System,
            protector);

        writer.EnqueueSensitiveNotification(
            user.Id,
            "Password reset",
            sensitiveMessage);
        await context.SaveChangesAsync();

        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.Equal(
            OutboxMessageTypes.ProtectedNotificationRequested,
            outboxMessage.Type);
        Assert.DoesNotContain(
            sensitiveMessage,
            outboxMessage.Payload,
            StringComparison.Ordinal);

        var sender = new RecordingNotificationSender();
        var handler = new NotificationOutboxMessageHandler(
            new UserRepository(context),
            sender,
            NullLogger<NotificationOutboxMessageHandler>.Instance,
            protector);
        await handler.HandleAsync(outboxMessage);

        var delivered = Assert.Single(sender.Messages);
        Assert.Equal(user.Email, delivered.Recipient);
        Assert.Equal(sensitiveMessage, delivered.Message);
    }

    [Fact]
    public async Task OutboxProcessor_FailureUsesInjectedClockForLeaseAndRetry()
    {
        await using var context = TestAppDbContext.Create();
        var (_, user) = await SeedPaymentAsync(context, "generic-hmac", "txn-outbox-clock");
        var now = new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        new OutboxWriter(
            new OutboxRepository(context),
            clock).EnqueueNotification(user.Id, "Subject", "Message");
        await context.SaveChangesAsync();
        var processor = new OutboxProcessor(
            new EfOutboxStore(context),
            new NotificationOutboxMessageHandler(
                new UserRepository(context),
                new AlwaysFailingNotificationSender(),
                NullLogger<NotificationOutboxMessageHandler>.Instance),
            Options.Create(new OutboxOptions
            {
                BatchSize = 1,
                MaxAttempts = 3,
                LockTimeoutMinutes = 5,
                ProcessingTimeoutSeconds = 30,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance,
            clock);

        Assert.Equal(1, await processor.ProcessBatchAsync());

        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(now.UtcDateTime, message.LastAttemptAt);
        Assert.Equal(now.UtcDateTime.AddSeconds(5), message.NextAttemptAt);
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.LockId);
    }

    [Fact]
    public async Task OutboxProcessor_DispatchesAndMarksNotificationProcessed()
    {
        await using var context = TestAppDbContext.Create();
        var (_, user) = await SeedPaymentAsync(context, "generic-hmac", "txn-002");
        var writer = new OutboxWriter(new OutboxRepository(context));
        writer.EnqueueNotification(user.Id, "Subject", "Message");
        await context.SaveChangesAsync();
        var sender = new RecordingNotificationSender();
        var handler = new NotificationOutboxMessageHandler(
            new UserRepository(context),
            sender,
            NullLogger<NotificationOutboxMessageHandler>.Instance);
        var processor = new OutboxProcessor(
            new EfOutboxStore(context),
            handler,
            Options.Create(new OutboxOptions
            {
                BatchSize = 10,
                MaxAttempts = 3,
                LockTimeoutMinutes = 5,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance);

        var processed = await processor.ProcessBatchAsync();

        Assert.Equal(1, processed);
        Assert.Single(sender.Messages);
        var outboxMessage = await context.OutboxMessages.SingleAsync();
        Assert.NotNull(outboxMessage.ProcessedAt);
        Assert.Null(outboxMessage.LockId);
        Assert.Equal(0, outboxMessage.Attempts);
    }

    [Fact]
    public async Task OutboxProcessor_RetriesThenDeadLettersFailedNotification()
    {
        await using var context = TestAppDbContext.Create();
        var (_, user) = await SeedPaymentAsync(context, "generic-hmac", "txn-outbox-failure");
        new OutboxWriter(new OutboxRepository(context))
            .EnqueueNotification(user.Id, "Subject", "Message");
        await context.SaveChangesAsync();

        var sender = new AlwaysFailingNotificationSender();
        var processor = CreateOutboxProcessor(context, sender, maxAttempts: 2);

        Assert.Equal(1, await processor.ProcessBatchAsync());

        var message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.DeadLetteredAt);
        Assert.Null(message.LockId);
        Assert.NotNull(message.LastAttemptAt);
        Assert.True(message.NextAttemptAt > message.LastAttemptAt);

        message.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        Assert.Equal(1, await processor.ProcessBatchAsync());

        message = await context.OutboxMessages.SingleAsync();
        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.DeadLetteredAt);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.LockId);
        Assert.Equal(2, sender.Attempts);
    }

    [Fact]
    public async Task OutboxProcessor_TimesOutHungHandlerAndSchedulesRetry()
    {
        await using var context = TestAppDbContext.Create();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxMessageTypes.NotificationRequested,
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var processor = new OutboxProcessor(
            new EfOutboxStore(context),
            new HangingOutboxMessageHandler(),
            Options.Create(new OutboxOptions
            {
                BatchSize = 1,
                MaxAttempts = 3,
                LockTimeoutMinutes = 5,
                ProcessingTimeoutSeconds = 1,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance);

        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Equal(1, message.Attempts);
        Assert.Null(message.LockId);
        Assert.Null(message.DeadLetteredAt);
        Assert.True(message.NextAttemptAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task OutboxProcessor_ShutdownReleasesLeaseWithoutConsumingAttempt()
    {
        await using var context = TestAppDbContext.Create();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxMessageTypes.NotificationRequested,
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        var handler = new BlockingOutboxMessageHandler();
        var processor = new OutboxProcessor(
            new EfOutboxStore(context),
            handler,
            Options.Create(new OutboxOptions
            {
                BatchSize = 1,
                MaxAttempts = 3,
                LockTimeoutMinutes = 5,
                ProcessingTimeoutSeconds = 30,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance);
        using var stopping = new CancellationTokenSource();

        var processing = processor.ProcessBatchAsync(stopping.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        context.ChangeTracker.Clear();
        var persisted = await context.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Null(persisted.LockId);
        Assert.Null(persisted.LockedAt);
        Assert.Equal(0, persisted.Attempts);
        Assert.Null(persisted.ProcessedAt);
        Assert.Null(persisted.DeadLetteredAt);
    }

    [Fact]
    public async Task OutboxProcessor_ReclaimsStaleLeaseAndDeliversOnce()
    {
        await using var context = TestAppDbContext.Create();
        var (_, user) = await SeedPaymentAsync(context, "generic-hmac", "txn-outbox-stale");
        new OutboxWriter(new OutboxRepository(context))
            .EnqueueNotification(user.Id, "Subject", "Message");
        await context.SaveChangesAsync();

        var message = await context.OutboxMessages.SingleAsync();
        message.LockId = Guid.NewGuid();
        message.LockedAt = DateTime.UtcNow.AddMinutes(-10);
        await context.SaveChangesAsync();

        var sender = new RecordingNotificationSender();
        var processor = CreateOutboxProcessor(context, sender);

        Assert.Equal(1, await processor.ProcessBatchAsync());
        Assert.Single(sender.Messages);
        Assert.NotNull(message.ProcessedAt);
        Assert.Null(message.LockId);
    }

    [Fact]
    public async Task OutboxStore_RejectsCompletionFromNonOwner()
    {
        await using var context = TestAppDbContext.Create();
        var message = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxMessageTypes.NotificationRequested,
            Payload = "{}",
            OccurredAt = DateTime.UtcNow,
            NextAttemptAt = DateTime.UtcNow
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        var store = new EfOutboxStore(context);
        var ownerLock = Guid.NewGuid();
        var claimed = await store.ClaimBatchAsync(
            ownerLock,
            1,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(-5));

        Assert.Single(claimed);
        Assert.False(await store.MarkProcessedAsync(
            message.Id,
            Guid.NewGuid(),
            DateTime.UtcNow));
        Assert.True(await store.MarkProcessedAsync(
            message.Id,
            ownerLock,
            DateTime.UtcNow));
    }

    [Fact]
    public async Task OutboxHealthCheck_ReportsStaleBacklogAndDeadLetters()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var stale = new OutboxMessage
        {
            Id = Guid.NewGuid(),
            Type = OutboxMessageTypes.NotificationRequested,
            Payload = "{}",
            OccurredAt = now.UtcDateTime.AddMinutes(-2),
            NextAttemptAt = now.UtcDateTime.AddMinutes(-2)
        };
        context.OutboxMessages.Add(stale);
        await context.SaveChangesAsync();

        var healthCheck = new OutboxHealthCheck(
            context,
            Options.Create(new OutboxOptions
            {
                Enabled = true,
                MaxPendingAgeMinutes = 1
            }),
            NullLogger<OutboxHealthCheck>.Instance,
            clock,
            new OutboxWorkerStatus());

        var staleResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Unhealthy, staleResult.Status);
        Assert.Equal(2d, staleResult.Data["oldestPendingAgeMinutes"]);

        stale.DeadLetteredAt = now.UtcDateTime;
        stale.Attempts = 1;
        await context.SaveChangesAsync();

        var deadLetterResult = await healthCheck.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, deadLetterResult.Status);
        Assert.Equal(1, deadLetterResult.Data["deadLetterCount"]);
    }

    private static OutboxProcessor CreateOutboxProcessor(
        Infrastructure.Data.AppDbContext context,
        INotificationSender sender,
        int maxAttempts = 3)
        => new(
            new EfOutboxStore(context),
            new NotificationOutboxMessageHandler(
                new UserRepository(context),
                sender,
                NullLogger<NotificationOutboxMessageHandler>.Instance),
            Options.Create(new OutboxOptions
            {
                BatchSize = 10,
                MaxAttempts = maxAttempts,
                LockTimeoutMinutes = 5,
                ProcessingTimeoutSeconds = 30,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance);
    private static IOptions<PaymentWebhookOptions> PaymentOptions(bool retainRawPayload = false)
        => Options.Create(new PaymentWebhookOptions
        {
            Enabled = true,
            ProviderCode = "generic-hmac",
            Secret = WebhookSecret,
            MaxPayloadBytes = 65_536,
            MaxFutureSkewMinutes = 5,
            RetainRawPayload = retainRawPayload
        });

    private static string Sign(string eventId, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(WebhookSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{eventId}.{payload}")));
    }

    private static async Task<(Payment Payment, User User)> SeedPaymentAsync(
        Infrastructure.Data.AppDbContext context,
        string provider,
        string providerTransactionId,
        DateTime? createdAt = null)
    {
        var paymentCreatedAt = createdAt ?? DateTime.UtcNow.AddMinutes(-1);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = $"payment_{Guid.NewGuid():N}"[..30],
            NormalizedUserName = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Email = $"{Guid.NewGuid():N}@example.com",
            NormalizedEmail = $"{Guid.NewGuid():N}@EXAMPLE.COM",
            FullName = "Payment Customer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = paymentCreatedAt
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IdempotencyRequestHash = new string('A', 64),
            OrderDate = paymentCreatedAt,
            ShippingAddress = "Address"
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = PaymentMethod.CashOnDelivery,
            Amount = 100m,
            Provider = provider,
            ProviderTransactionId = providerTransactionId,
            CreatedAt = paymentCreatedAt
        };
        context.AddRange(user, order, payment, new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            ChangedByUserId = user.Id,
            FromStatus = null,
            ToStatus = payment.Status,
            Source = PaymentStatusChangeSource.Checkout,
            Reference = order.OrderNumber,
            OccurredAt = payment.CreatedAt,
            CreatedAt = payment.CreatedAt
        });
        await context.SaveChangesAsync();
        return (payment, user);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
    private sealed class HangingOutboxMessageHandler : IOutboxMessageHandler
    {
        public Task HandleAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
            => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
    private sealed class BlockingOutboxMessageHandler : IOutboxMessageHandler
    {
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task HandleAsync(
            OutboxMessage message,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
    private sealed class AlwaysFailingNotificationSender : INotificationSender
    {
        public int Attempts { get; private set; }

        public Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new InvalidOperationException("Simulated notification delivery failure.");
        }
    }
    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<(string Recipient, string Subject, string Message, Guid IdempotencyKey)> Messages { get; } = [];

        public Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((recipientEmail, subject, message, idempotencyKey));
            return Task.CompletedTask;
        }
    }
    private sealed class StubPaymentProvider(
        string code,
        PaymentMethod? checkoutMethod,
        bool supportsWebhooks,
        PaymentInitializationResult initialization) : IPaymentProvider
    {
        public string Code => code;
        public PaymentMethod? CheckoutMethod => checkoutMethod;
        public bool SupportsWebhooks => supportsWebhooks;

        public PaymentInitializationResult Initialize(PaymentInitializationRequest request)
            => initialization;

        public Task<VerifiedPaymentWebhook> VerifyWebhookAsync(
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
