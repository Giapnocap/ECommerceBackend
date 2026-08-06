using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class DataRetentionUseCase
    {
        private static readonly Meter Meter = new("ECommerceBackend.Operations");
        private static readonly ActivitySource ActivitySource = new("ECommerceBackend.Operations");
        private static readonly Counter<long> RetentionRunCounter =
            Meter.CreateCounter<long>("data_retention.runs");
        private static readonly Counter<long> RetentionChangedCounter =
            Meter.CreateCounter<long>("data_retention.records.changed");
        private static readonly Counter<long> RetentionLockContentionCounter =
            Meter.CreateCounter<long>("data_retention.lock_contentions");
        private static readonly Histogram<double> RetentionDuration =
            Meter.CreateHistogram<double>("data_retention.duration", "ms");

        private readonly IDataRetentionRepository _retentionRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;
        private readonly DataRetentionOptions _retentionOptions;
        private readonly ILogger<DataRetentionUseCase> _logger;

        public DataRetentionUseCase(
            IDataRetentionRepository retentionRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider,
            IOptions<DataRetentionOptions> retentionOptions,
            ILogger<DataRetentionUseCase> logger)
        {
            _retentionRepository = retentionRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
            _retentionOptions = retentionOptions.Value;
            _logger = logger;
        }

        public async Task<DataRetentionResponse> ExecuteAsync(
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
                    var previewCandidates = await _retentionRepository.LoadCandidatesAsync(
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
                var candidates = await _retentionRepository.LoadCandidatesAsync(
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

                _retentionRepository.Apply(candidates);

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

                await _unitOfWork.SaveChangesAsync(cancellationToken);
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

        private static void RecordRetentionChanges(DataRetentionBatch candidates)
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
            DataRetentionBatch candidates)
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

        private DataRetentionResponse CreateRetentionResponse(
            DataRetentionBatch candidates,
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

    }
}
