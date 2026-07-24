using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OperationsService : IOperationsService
    {
        private static readonly Meter Meter = new("ECommerceBackend.Operations");
        private static readonly ActivitySource ActivitySource = new("ECommerceBackend.Operations");
        private static readonly Counter<long> RedriveCounter = Meter.CreateCounter<long>("outbox.dead_letters.redriven");
        private static readonly Counter<long> RetentionRunCounter =
            Meter.CreateCounter<long>("data_retention.runs");
        private static readonly Counter<long> RetentionChangedCounter =
            Meter.CreateCounter<long>("data_retention.records.changed");
        private static readonly Counter<long> RetentionLockContentionCounter =
            Meter.CreateCounter<long>("data_retention.lock_contentions");
        private static readonly Histogram<double> RetentionDuration =
            Meter.CreateHistogram<double>("data_retention.duration", "ms");

        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;
        private readonly DataRetentionOptions _retentionOptions;
        private readonly ILogger<OperationsService> _logger;

        public OperationsService(
            IAppDbContext context,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider,
            IOptions<DataRetentionOptions> retentionOptions,
            ILogger<OperationsService> logger)
        {
            _context = context;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
            _retentionOptions = retentionOptions.Value;
            _logger = logger;
        }

        public async Task<PagedResult<DeadLetterResponse>> GetDeadLettersAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var messages = _context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.DeadLetteredAt != null);
            var totalCount = await messages.CountAsync(cancellationToken);
            var items = await messages
                .OrderByDescending(message => message.DeadLetteredAt)
                .ThenBy(message => message.Id)
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(message => new DeadLetterResponse
                {
                    Id = message.Id,
                    Type = message.Type,
                    OccurredAt = message.OccurredAt,
                    Attempts = message.Attempts,
                    LastAttemptAt = message.LastAttemptAt,
                    DeadLetteredAt = message.DeadLetteredAt,
                    LastError = message.LastError
                })
                .ToListAsync(cancellationToken);

            return PagedResult<DeadLetterResponse>.Create(items, totalCount, paging.Page, paging.Size);
        }

        public async Task<RedriveOutboxResponse> RedriveDeadLetterAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;

            try
            {
                var message = await _consistency.LockOutboxMessageAsync(messageId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy thông báo trong hàng đợi.");

                if (message.ProcessedAt.HasValue)
                    throw new ConflictException("Không thể gửi lại thông báo đã được xử lý thành công.");

                if (!message.DeadLetteredAt.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                    return new RedriveOutboxResponse
                    {
                        Id = message.Id,
                        ReDriven = false,
                        NextAttemptAt = message.NextAttemptAt
                    };
                }

                var previousAttempts = message.Attempts;
                var previousDeadLetteredAt = message.DeadLetteredAt;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                message.Attempts = 0;
                message.NextAttemptAt = now;
                message.DeadLetteredAt = null;
                message.LockId = null;
                message.LockedAt = null;
                message.LastError = null;

                _audit.Write(
                    "outbox.dead_letter.redrive",
                    "OutboxMessage",
                    message.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["previousAttempts"] = previousAttempts,
                        ["previousDeadLetteredAt"] = previousDeadLetteredAt,
                        ["messageType"] = message.Type
                    });

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                RedriveCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));

                return new RedriveOutboxResponse
                {
                    Id = message.Id,
                    ReDriven = true,
                    NextAttemptAt = now
                };
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var events = _context.AuditEvents.AsNoTracking().AsQueryable();

            if (query.ActorUserId.HasValue)
                events = events.Where(item => item.ActorUserId == query.ActorUserId);
            if (!string.IsNullOrWhiteSpace(query.Action))
                events = events.Where(item => item.Action == query.Action.Trim());
            if (!string.IsNullOrWhiteSpace(query.EntityType))
                events = events.Where(item => item.EntityType == query.EntityType.Trim());
            if (query.From.HasValue)
                events = events.Where(item => item.CreatedAt >= query.From.Value);
            if (query.To.HasValue)
                events = events.Where(item => item.CreatedAt < query.To.Value);

            var totalCount = await events.CountAsync(cancellationToken);
            var items = await events
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(item => new AuditEventResponse
                {
                    Id = item.Id,
                    ActorUserId = item.ActorUserId,
                    Action = item.Action,
                    EntityType = item.EntityType,
                    EntityId = item.EntityId,
                    CorrelationId = item.CorrelationId,
                    IpAddress = item.IpAddress,
                    MetadataJson = item.MetadataJson,
                    CreatedAt = item.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return PagedResult<AuditEventResponse>.Create(items, totalCount, paging.Page, paging.Size);
        }

        public async Task<DataRetentionResponse> RunDataRetentionAsync(
            DataRetentionRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken = default)
        {
            var startedTimestamp = Stopwatch.GetTimestamp();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var batchSize = Math.Clamp(request.MaxBatchSize, 1, _retentionOptions.MaxBatchSize);
            var processedOutboxCutoff = now.AddDays(-_retentionOptions.ProcessedOutboxRetentionDays);
            var expiredRefreshTokenCutoff = now.AddDays(-_retentionOptions.ExpiredRefreshTokenRetentionDays);
            var webhookPayloadCutoff = now.AddDays(-_retentionOptions.WebhookPayloadRetentionDays);

            var applyChanges = request.ApplyChanges && _retentionOptions.Enabled;
            var mode = applyChanges ? "apply" : "preview";
            using var activity = ActivitySource.StartActivity("data-retention.run", ActivityKind.Internal);
            activity?.SetTag("data_retention.mode", mode);
            activity?.SetTag("data_retention.batch_size", batchSize);

            if (!applyChanges)
            {
                try
                {
                    var previewCandidates = await LoadRetentionCandidatesAsync(
                        batchSize,
                        processedOutboxCutoff,
                        expiredRefreshTokenCutoff,
                        webhookPayloadCutoff,
                        cancellationToken);
                    SetRetentionActivityCounts(activity, previewCandidates);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    RecordRetentionRun(mode, "success", startedTimestamp);
                    _logger.LogInformation(
                        "Đã xem trước chính sách lưu giữ dữ liệu. Outbox có thể xóa: {ProcessedOutboxCandidates}, mã làm mới có thể xóa: {ExpiredRefreshTokenCandidates}, payload webhook có thể xóa: {WebhookPayloadCandidates}, kích thước lô: {BatchSize}.",
                        previewCandidates.ProcessedOutbox.Count,
                        previewCandidates.ExpiredRefreshTokens.Count,
                        previewCandidates.WebhookPayloads.Count,
                        batchSize);
                    return CreateRetentionResponse(
                        previewCandidates,
                        dryRun: true,
                        processedOutboxCutoff,
                        expiredRefreshTokenCutoff,
                        webhookPayloadCutoff);
                }
                catch (Exception ex)
                {
                    var result = GetRetentionFailureResult(ex, cancellationToken);
                    activity?.SetStatus(ActivityStatusCode.Error, result);
                    activity?.SetTag("error.type", ex.GetType().Name);
                    RecordRetentionRun(mode, result, startedTimestamp);
                    if (result == "failed")
                        _logger.LogWarning(ex, "Không thể xem trước chính sách lưu giữ dữ liệu.");
                    throw;
                }
            }

            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;

            try
            {
                if (!await _consistency.TryAcquireDataRetentionLockAsync(cancellationToken))
                {
                    RetentionLockContentionCounter.Add(1);
                    activity?.SetStatus(ActivityStatusCode.Error, "lock_conflict");
                    RecordRetentionRun(mode, "lock_conflict", startedTimestamp);
                    _logger.LogWarning("Không thể áp dụng chính sách lưu giữ dữ liệu vì một tác vụ khác đang giữ khóa vận hành.");
                    throw new ConflictException(
                        "Một tác vụ lưu giữ dữ liệu khác đang được thực hiện. Vui lòng thử lại sau.");
                }
                var candidates = await LoadRetentionCandidatesAsync(
                    batchSize,
                    processedOutboxCutoff,
                    expiredRefreshTokenCutoff,
                    webhookPayloadCutoff,
                    cancellationToken);
                var response = CreateRetentionResponse(
                    candidates,
                    dryRun: false,
                    processedOutboxCutoff,
                    expiredRefreshTokenCutoff,
                    webhookPayloadCutoff);

                _context.OutboxMessages.RemoveRange(candidates.ProcessedOutbox);
                _context.RefreshTokens.RemoveRange(candidates.ExpiredRefreshTokens);
                foreach (var webhook in candidates.WebhookPayloads)
                    webhook.Payload = string.Empty;

                var changedRecordCount = candidates.ProcessedOutbox.Count
                    + candidates.ExpiredRefreshTokens.Count
                    + candidates.WebhookPayloads.Count;
                if (changedRecordCount > 0)
                {
                    _audit.Write(
                        "operations.data_retention.apply",
                        "DataRetention",
                        null,
                        actorUserId,
                        new Dictionary<string, object?>
                        {
                            ["processedOutboxDeleted"] = candidates.ProcessedOutbox.Count,
                            ["expiredRefreshTokensDeleted"] = candidates.ExpiredRefreshTokens.Count,
                            ["webhookPayloadsRedacted"] = candidates.WebhookPayloads.Count,
                            ["batchSize"] = batchSize,
                            ["processedOutboxRetentionDays"] = _retentionOptions.ProcessedOutboxRetentionDays,
                            ["expiredRefreshTokenRetentionDays"] = _retentionOptions.ExpiredRefreshTokenRetentionDays,
                            ["webhookPayloadRetentionDays"] = _retentionOptions.WebhookPayloadRetentionDays
                        });
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;

                response.ProcessedOutboxDeletedCount = candidates.ProcessedOutbox.Count;
                response.ExpiredRefreshTokenDeletedCount = candidates.ExpiredRefreshTokens.Count;
                response.WebhookPayloadRedactedCount = candidates.WebhookPayloads.Count;
                SetRetentionActivityCounts(activity, candidates);
                activity?.SetStatus(ActivityStatusCode.Ok);
                RecordRetentionChanges(candidates);
                RecordRetentionRun(mode, "success", startedTimestamp);
                _logger.LogInformation(
                    "Đã áp dụng chính sách lưu giữ dữ liệu. Outbox đã xóa: {ProcessedOutboxDeleted}, mã làm mới đã xóa: {ExpiredRefreshTokensDeleted}, payload webhook đã xóa: {WebhookPayloadsRedacted}, kích thước lô: {BatchSize}.",
                    candidates.ProcessedOutbox.Count,
                    candidates.ExpiredRefreshTokens.Count,
                    candidates.WebhookPayloads.Count,
                    batchSize);
                return response;
            }
            catch (Exception ex)
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);

                if (ex is not ConflictException)
                {
                    var result = GetRetentionFailureResult(ex, cancellationToken);
                    activity?.SetStatus(ActivityStatusCode.Error, result);
                    activity?.SetTag("error.type", ex.GetType().Name);
                    RecordRetentionRun(mode, result, startedTimestamp);
                    if (result == "failed")
                        _logger.LogWarning(ex, "Áp dụng chính sách lưu giữ dữ liệu thất bại và transaction đã được hoàn tác.");
                }
                throw;
            }
        }

        private static void RecordRetentionRun(string mode, string result, long startedTimestamp)
        {
            var tags = new TagList
            {
                { "mode", mode },
                { "result", result }
            };
            RetentionRunCounter.Add(1, tags);
            RetentionDuration.Record(
                Stopwatch.GetElapsedTime(startedTimestamp).TotalMilliseconds,
                tags);
        }

        private static void RecordRetentionChanges(DataRetentionCandidates candidates)
        {
            RecordRetentionChange("processed_outbox", candidates.ProcessedOutbox.Count);
            RecordRetentionChange("expired_refresh_token", candidates.ExpiredRefreshTokens.Count);
            RecordRetentionChange("webhook_payload", candidates.WebhookPayloads.Count);
        }

        private static void RecordRetentionChange(string recordType, int count)
        {
            if (count > 0)
            {
                RetentionChangedCounter.Add(
                    count,
                    new KeyValuePair<string, object?>("record.type", recordType));
            }
        }

        private static void SetRetentionActivityCounts(
            Activity? activity,
            DataRetentionCandidates candidates)
        {
            activity?.SetTag("data_retention.processed_outbox_count", candidates.ProcessedOutbox.Count);
            activity?.SetTag("data_retention.expired_refresh_token_count", candidates.ExpiredRefreshTokens.Count);
            activity?.SetTag("data_retention.webhook_payload_count", candidates.WebhookPayloads.Count);
        }

        private static string GetRetentionFailureResult(
            Exception exception,
            CancellationToken cancellationToken)
            => exception is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? "cancelled"
                : "failed";

        private async Task<DataRetentionCandidates> LoadRetentionCandidatesAsync(
            int batchSize,
            DateTime processedOutboxCutoff,
            DateTime expiredRefreshTokenCutoff,
            DateTime webhookPayloadCutoff,
            CancellationToken cancellationToken)
        {
            var processedOutbox = await _context.OutboxMessages
                .Where(message => message.ProcessedAt != null && message.ProcessedAt < processedOutboxCutoff)
                .OrderBy(message => message.ProcessedAt)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            var expiredRefreshTokens = await _context.RefreshTokens
                .Where(token => token.ExpiresAt < expiredRefreshTokenCutoff)
                .OrderBy(token => token.ExpiresAt)
                .ThenBy(token => token.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            var webhookPayloads = await _context.PaymentWebhookEvents
                .Where(webhook => webhook.ReceivedAt < webhookPayloadCutoff && webhook.Payload != string.Empty)
                .OrderBy(webhook => webhook.ReceivedAt)
                .ThenBy(webhook => webhook.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            return new DataRetentionCandidates(processedOutbox, expiredRefreshTokens, webhookPayloads);
        }

        private DataRetentionResponse CreateRetentionResponse(
            DataRetentionCandidates candidates,
            bool dryRun,
            DateTime processedOutboxCutoff,
            DateTime expiredRefreshTokenCutoff,
            DateTime webhookPayloadCutoff)
            => new()
            {
                DryRun = dryRun,
                ApplyChangesEnabled = _retentionOptions.Enabled,
                ProcessedOutboxCandidateCount = candidates.ProcessedOutbox.Count,
                ExpiredRefreshTokenCandidateCount = candidates.ExpiredRefreshTokens.Count,
                WebhookPayloadCandidateCount = candidates.WebhookPayloads.Count,
                ProcessedOutboxCutoff = processedOutboxCutoff,
                ExpiredRefreshTokenCutoff = expiredRefreshTokenCutoff,
                WebhookPayloadCutoff = webhookPayloadCutoff
            };

        private sealed record DataRetentionCandidates(
            List<OutboxMessage> ProcessedOutbox,
            List<RefreshToken> ExpiredRefreshTokens,
            List<PaymentWebhookEvent> WebhookPayloads);
    }
}
