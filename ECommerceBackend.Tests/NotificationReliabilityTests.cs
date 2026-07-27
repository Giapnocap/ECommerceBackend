using System.Net;
using System.Net.Sockets;
using System.Text;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class NotificationReliabilityTests
{
    [Fact]
    public async Task SmtpRetry_UsesSameDeterministicMessageId()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var captureTask = CaptureMessagesAsync(listener, 2, timeout.Token);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var sender = new ConfigurableNotificationSender(
            Options.Create(new SmtpOptions
            {
                Enabled = true,
                Host = IPAddress.Loopback.ToString(),
                Port = endpoint.Port,
                EnableSsl = false,
                TimeoutSeconds = 5,
                FromAddress = "no-reply@example.com",
                FromName = "ECommerceBackend"
            }),
            NullLogger<ConfigurableNotificationSender>.Instance);
        var outboxMessageId = Guid.Parse("52b1842f-c58b-4301-8777-69a8fa930810");

        try
        {
            await sender.SendAsync(
                "customer@example.com",
                "Đơn hàng",
                "Đơn hàng đã được xác nhận.",
                outboxMessageId,
                timeout.Token);
            await sender.SendAsync(
                "customer@example.com",
                "Đơn hàng",
                "Đơn hàng đã được xác nhận.",
                outboxMessageId,
                timeout.Token);
            var messages = await captureTask;

            Assert.Equal(2, messages.Count);
            var expectedMessageId =
                $"<{outboxMessageId:N}@notifications.ecommercebackend.local>";
            Assert.All(
                messages,
                message =>
                {
                    Assert.Equal(
                        expectedMessageId,
                        GetHeader(message, "Message-ID"));
                    Assert.Equal(
                        outboxMessageId.ToString("N"),
                        GetHeader(message, "X-Idempotency-Key"));
                });
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CrashAfterDelivery_ReclaimsLeaseAndRedeliversSameOutboxMessage()
    {
        var now = new DateTimeOffset(2026, 7, 24, 15, 0, 0, TimeSpan.Zero);
        var clock = new MutableTimeProvider(now);
        await using var context = TestAppDbContext.Create();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "notification_customer",
            NormalizedUserName = "NOTIFICATION_CUSTOMER",
            Email = "notification_customer@example.com",
            NormalizedEmail = "NOTIFICATION_CUSTOMER@EXAMPLE.COM",
            PasswordHash = "test-hash",
            FullName = "Notification Customer",
            CreatedAt = now.UtcDateTime
        };
        context.Users.Add(user);
        new OutboxWriter(
            new OutboxRepository(context),
            clock).EnqueueNotification(
            user.Id,
            "Subject",
            "Message");
        await context.SaveChangesAsync();
        var messageId = await context.OutboxMessages
            .Select(message => message.Id)
            .SingleAsync();
        var sender = new RecordingNotificationSender();
        var handler = new NotificationOutboxMessageHandler(
            new UserRepository(context),
            sender,
            NullLogger<NotificationOutboxMessageHandler>.Instance);
        var crashStore = new CrashAfterDeliveryOutboxStore(
            new EfOutboxStore(context));
        var crashingProcessor = CreateProcessor(
            crashStore,
            handler,
            clock);

        await Assert.ThrowsAsync<SimulatedProcessCrashException>(() =>
            crashingProcessor.ProcessBatchAsync());

        context.ChangeTracker.Clear();
        var pending = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync();
        Assert.Null(pending.ProcessedAt);
        Assert.NotNull(pending.LockId);
        Assert.Equal(0, pending.Attempts);
        Assert.Equal([messageId], sender.MessageIds);

        clock.Advance(TimeSpan.FromMinutes(6));
        var recoveryProcessor = CreateProcessor(
            new EfOutboxStore(context),
            handler,
            clock);

        Assert.Equal(1, await recoveryProcessor.ProcessBatchAsync());

        context.ChangeTracker.Clear();
        var processed = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync();
        Assert.NotNull(processed.ProcessedAt);
        Assert.Null(processed.LockId);
        Assert.Equal([messageId, messageId], sender.MessageIds);
    }

    private static OutboxProcessor CreateProcessor(
        IOutboxStore store,
        IOutboxMessageHandler handler,
        TimeProvider timeProvider)
        => new(
            store,
            handler,
            Options.Create(new OutboxOptions
            {
                BatchSize = 1,
                MaxAttempts = 3,
                LockTimeoutMinutes = 5,
                ProcessingTimeoutSeconds = 30,
                PollIntervalSeconds = 1
            }),
            NullLogger<OutboxProcessor>.Instance,
            timeProvider);

    private static async Task<IReadOnlyList<string>> CaptureMessagesAsync(
        TcpListener listener,
        int count,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>(count);
        while (messages.Count < count)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                stream,
                Encoding.ASCII,
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n"
            };

            await writer.WriteLineAsync("220 localhost ESMTP");
            var data = new StringBuilder();
            var readingData = false;

            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (readingData)
                {
                    if (line == ".")
                    {
                        messages.Add(data.ToString());
                        readingData = false;
                        await writer.WriteLineAsync("250 2.0.0 accepted");
                    }
                    else
                    {
                        data.Append(line.StartsWith("..", StringComparison.Ordinal)
                            ? line[1..]
                            : line);
                        data.Append("\r\n");
                    }

                    continue;
                }

                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("250 localhost");
                }
                else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    readingData = true;
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("221 2.0.0 bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 2.0.0 ok");
                }
            }
        }

        return messages;
    }

    private static string GetHeader(string message, string name)
    {
        var prefix = name + ":";
        var line = message
            .Split("\r\n", StringSplitOptions.None)
            .Single(candidate =>
                candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line[prefix.Length..].Trim();
    }

    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<Guid> MessageIds { get; } = [];

        public Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            MessageIds.Add(idempotencyKey);
            return Task.CompletedTask;
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private sealed class CrashAfterDeliveryOutboxStore(IOutboxStore inner)
        : IOutboxStore
    {
        public Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
            Guid lockId,
            int batchSize,
            DateTime now,
            DateTime staleBefore,
            CancellationToken cancellationToken = default)
            => inner.ClaimBatchAsync(
                lockId,
                batchSize,
                now,
                staleBefore,
                cancellationToken);

        public Task<bool> MarkProcessedAsync(
            Guid messageId,
            Guid lockId,
            DateTime processedAt,
            CancellationToken cancellationToken = default)
            => throw new SimulatedProcessCrashException();

        public Task<bool> MarkFailedAsync(
            Guid messageId,
            Guid lockId,
            int attempts,
            DateTime nextAttemptAt,
            DateTime? deadLetteredAt,
            string error,
            CancellationToken cancellationToken = default)
            => throw new SimulatedProcessCrashException();

        public Task<bool> ReleaseClaimAsync(
            Guid messageId,
            Guid lockId,
            CancellationToken cancellationToken = default)
            => inner.ReleaseClaimAsync(messageId, lockId, cancellationToken);
    }

    private sealed class SimulatedProcessCrashException : Exception;
}
