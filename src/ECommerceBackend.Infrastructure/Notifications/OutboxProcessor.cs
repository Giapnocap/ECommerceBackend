using ECommerceBackend.Application.Common;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Notifications
{
    public sealed class OutboxProcessor
    {
        private static readonly Meter Meter = new("ECommerceBackend.Outbox");
        private static readonly Counter<long> ProcessedCounter = Meter.CreateCounter<long>("outbox.messages.processed");
        private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("outbox.messages.failed");
        private static readonly Counter<long> DeadLetterCounter = Meter.CreateCounter<long>("outbox.messages.dead_lettered");
        private readonly IOutboxStore _store;
        private readonly IOutboxMessageHandler _handler;
        private readonly OutboxOptions _options;
        private readonly ILogger<OutboxProcessor> _logger;
        private readonly TimeProvider _timeProvider;

        public OutboxProcessor(
            IOutboxStore store,
            IOutboxMessageHandler handler,
            IOptions<OutboxOptions> options,
            ILogger<OutboxProcessor> logger)
            : this(store, handler, options, logger, TimeProvider.System)
        {
        }

        public OutboxProcessor(
            IOutboxStore store,
            IOutboxMessageHandler handler,
            IOptions<OutboxOptions> options,
            ILogger<OutboxProcessor> logger,
            TimeProvider timeProvider)
        {
            _store = store;
            _handler = handler;
            _options = options.Value;
            _logger = logger;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken = default)
        {
            var handled = 0;

            while (handled < _options.BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var now = UtcNow;
                var lockId = Guid.NewGuid();
                var messages = await _store.ClaimBatchAsync(
                    lockId,
                    1,
                    now,
                    now.AddMinutes(-_options.LockTimeoutMinutes),
                    cancellationToken);
                var message = messages.SingleOrDefault();
                if (message is null)
                    break;

                handled++;

                using var processingCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                processingCts.CancelAfter(TimeSpan.FromSeconds(_options.ProcessingTimeoutSeconds));
                using var telemetry = BusinessTelemetry.Start(
                    "outbox.dispatch",
                    processingCts.Token,
                    new KeyValuePair<string, object?>(
                        "messaging.message.type",
                        message.Type));

                try
                {
                    await _handler.HandleAsync(message, processingCts.Token);
                    var marked = await _store.MarkProcessedAsync(
                        message.Id,
                        lockId,
                        UtcNow,
                        CancellationToken.None);
                    if (!marked)
                    {
                        _logger.LogWarning(
                            "Outbox message {OutboxMessageId} was delivered but its lease was no longer owned.",
                            message.Id);
                    }
                    else
                    {
                        ProcessedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                        telemetry.Complete();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    var released = await _store.ReleaseClaimAsync(
                        message.Id,
                        lockId,
                        CancellationToken.None);
                    if (!released)
                    {
                        _logger.LogWarning(
                            "Outbox message {OutboxMessageId} could not release its lease during shutdown.",
                            message.Id);
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    var attempts = message.Attempts + 1;
                    var failedAt = UtcNow;
                    var deadLetteredAt = attempts >= _options.MaxAttempts
                        ? failedAt
                        : (DateTime?)null;
                    var nextAttemptAt = deadLetteredAt ?? failedAt.Add(GetRetryDelay(attempts));
                    var marked = await _store.MarkFailedAsync(
                        message.Id,
                        lockId,
                        attempts,
                        nextAttemptAt,
                        deadLetteredAt,
                        Truncate(GetErrorMessage(ex), 2000),
                        CancellationToken.None);

                    if (!marked)
                    {
                        _logger.LogWarning(
                            "Outbox message {OutboxMessageId} failed but its lease was no longer owned.",
                            message.Id);
                    }
                    else
                    {
                        FailedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                        if (deadLetteredAt.HasValue)
                        {
                            DeadLetterCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                            telemetry.SetTag("outbox.dead_lettered", true);
                        }
                    }

                    _logger.LogError(
                        ex,
                        "Outbox message {OutboxMessageId} failed on attempt {Attempt}. DeadLettered: {DeadLettered}.",
                        message.Id,
                        attempts,
                        deadLetteredAt.HasValue);
                }
            }

            return handled;
        }

        private static TimeSpan GetRetryDelay(int attempts)
        {
            var exponent = Math.Clamp(attempts - 1, 0, 10);
            return TimeSpan.FromSeconds(Math.Min(3600, Math.Pow(2, exponent) * 5));
        }

        private static string GetErrorMessage(Exception exception)
            => exception switch
            {
                TimeoutException => "Quá thời gian xử lý thông báo.",
                _ => "Không thể xử lý thông báo. Xem nhật ký máy chủ để biết chi tiết."
            };

        private static string Truncate(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];
    }
}
