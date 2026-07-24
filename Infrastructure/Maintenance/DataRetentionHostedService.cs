using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Maintenance
{
    public sealed class DataRetentionHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly DataRetentionOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<DataRetentionHostedService> _logger;
        private readonly DataRetentionWorkerStatus _status;

        public DataRetentionHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<DataRetentionOptions> options,
            TimeProvider timeProvider,
            ILogger<DataRetentionHostedService> logger,
            DataRetentionWorkerStatus status)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _status = status;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.AutomaticProcessingEnabled)
            {
                _logger.LogInformation("Worker lưu giữ dữ liệu đang tắt.");
                return;
            }

            _status.MarkStarted(_timeProvider.GetUtcNow().UtcDateTime);
            _logger.LogInformation(
                "Worker lưu giữ dữ liệu đã khởi động. Kích thước lô: {BatchSize}, số lô tối đa: {MaxBatchesPerCycle}, chu kỳ: {ProcessingIntervalMinutes} phút.",
                _options.MaxBatchSize,
                _options.MaxBatchesPerCycle,
                _options.ProcessingIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                var nextDelay = TimeSpan.FromMinutes(_options.ProcessingIntervalMinutes);
                try
                {
                    var changedRecordCount = await ProcessCycleAsync(stoppingToken);
                    _status.MarkSuccessfulCycle(
                        _timeProvider.GetUtcNow().UtcDateTime,
                        changedRecordCount);
                    if (changedRecordCount > 0)
                    {
                        _logger.LogInformation(
                            "Worker lưu giữ dữ liệu đã xử lý {ChangedRecordCount} bản ghi trong chu kỳ.",
                            changedRecordCount);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _status.MarkFailure(_timeProvider.GetUtcNow().UtcDateTime);
                    _logger.LogError(ex, "Chu kỳ lưu giữ dữ liệu thất bại.");
                    nextDelay = TimeSpan.FromMinutes(_options.FailureRetryMinutes);
                }

                try
                {
                    await Task.Delay(
                        nextDelay,
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("Worker lưu giữ dữ liệu đã dừng.");
        }

        public async Task<int> ProcessCycleAsync(CancellationToken cancellationToken = default)
        {
            var totalChangedRecordCount = 0;
            for (var batch = 0; batch < _options.MaxBatchesPerCycle; batch++)
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();
                var response = await operations.RunDataRetentionAsync(
                    new DataRetentionRequest
                    {
                        ApplyChanges = true,
                        MaxBatchSize = _options.MaxBatchSize
                    },
                    actorUserId: null,
                    cancellationToken);
                var changedRecordCount = response.ProcessedOutboxDeletedCount
                    + response.ExpiredRefreshTokenDeletedCount
                    + response.WebhookPayloadRedactedCount;
                totalChangedRecordCount += changedRecordCount;

                var hasFullCandidateBatch = response.ProcessedOutboxCandidateCount >= _options.MaxBatchSize
                    || response.ExpiredRefreshTokenCandidateCount >= _options.MaxBatchSize
                    || response.WebhookPayloadCandidateCount >= _options.MaxBatchSize;
                if (changedRecordCount == 0 || !hasFullCandidateBatch)
                    break;
            }

            return totalChangedRecordCount;
        }
    }
}
