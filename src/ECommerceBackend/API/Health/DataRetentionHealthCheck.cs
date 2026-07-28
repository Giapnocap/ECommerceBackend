using ECommerceBackend.Application.Common;
using ECommerceBackend.Infrastructure.Maintenance;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.API.Health
{
    public sealed class DataRetentionHealthCheck : IHealthCheck
    {
        private readonly DataRetentionOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly DataRetentionWorkerStatus _workerStatus;

        public DataRetentionHealthCheck(
            IOptions<DataRetentionOptions> options,
            TimeProvider timeProvider,
            DataRetentionWorkerStatus workerStatus)
        {
            _options = options.Value;
            _timeProvider = timeProvider;
            _workerStatus = workerStatus;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            if (!_options.AutomaticProcessingEnabled && _options.RequireAutomaticProcessing)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Xử lý lưu giữ dữ liệu tự động là bắt buộc nhưng worker đang tắt."));
            }

            if (!_options.AutomaticProcessingEnabled)
                return Task.FromResult(HealthCheckResult.Healthy("Worker lưu giữ dữ liệu đang tắt."));

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var worker = _workerStatus.GetSnapshot();
            var heartbeatAgeLimit = TimeSpan.FromMinutes(
                Math.Max(5, _options.ProcessingIntervalMinutes * 2));
            var data = new Dictionary<string, object>
            {
                ["lastChangedRecordCount"] = worker.LastChangedRecordCount
            };
            if (worker.StartedAt.HasValue)
                data["workerStartedAt"] = worker.StartedAt.Value;
            if (worker.LastSuccessfulCycleAt.HasValue)
                data["lastSuccessfulCycleAt"] = worker.LastSuccessfulCycleAt.Value;
            if (worker.LastFailureAt.HasValue)
                data["lastFailureAt"] = worker.LastFailureAt.Value;

            var heartbeatMissing = !worker.LastSuccessfulCycleAt.HasValue
                || now - worker.LastSuccessfulCycleAt.Value > heartbeatAgeLimit;
            var latestCycleFailed = worker.LastFailureAt.HasValue
                && (!worker.LastSuccessfulCycleAt.HasValue
                    || worker.LastFailureAt.Value > worker.LastSuccessfulCycleAt.Value);
            if (latestCycleFailed && _options.RequireAutomaticProcessing)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Chu kỳ lưu giữ dữ liệu gần nhất đã thất bại.",
                    data: data));
            }

            if (latestCycleFailed)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Chu kỳ lưu giữ dữ liệu gần nhất đã thất bại.",
                    data: data));
            }

            if (heartbeatMissing && _options.RequireAutomaticProcessing)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Worker lưu giữ dữ liệu chưa có heartbeat hợp lệ.",
                    data: data));
            }

            if (heartbeatMissing)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "Worker lưu giữ dữ liệu chưa có heartbeat hợp lệ.",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Worker lưu giữ dữ liệu đang hoạt động bình thường.",
                data));
        }
    }
}
