using ECommerceBackend.Application.Common;
using ECommerceBackend.Infrastructure.Payments;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.API.Health
{
    public sealed class PaymentReconciliationHealthCheck : IHealthCheck
    {
        private readonly StripePaymentOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly PaymentReconciliationWorkerStatus _workerStatus;

        public PaymentReconciliationHealthCheck(
            IOptions<StripePaymentOptions> options,
            TimeProvider timeProvider,
            PaymentReconciliationWorkerStatus workerStatus)
        {
            _options = options.Value;
            _timeProvider = timeProvider;
            _workerStatus = workerStatus;
        }

        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_options.ReconciliationEnabled)
            {
                var disabledResult = _options.RequireReconciliation
                    ? HealthCheckResult.Unhealthy(
                        "Đối soát thanh toán là bắt buộc nhưng tiến trình nền đang tắt.")
                    : HealthCheckResult.Healthy(
                        "Tiến trình đối soát thanh toán đang tắt.");
                return Task.FromResult(disabledResult);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var worker = _workerStatus.GetSnapshot();
            var heartbeatAgeLimit = TimeSpan.FromSeconds(
                Math.Max(
                    30,
                    _options.ReconciliationPollIntervalSeconds * 3));
            var data = new Dictionary<string, object>();
            if (worker.StartedAt.HasValue)
                data["workerStartedAt"] = worker.StartedAt.Value;
            if (worker.LastSuccessfulCycleAt.HasValue)
            {
                data["lastSuccessfulCycleAt"] =
                    worker.LastSuccessfulCycleAt.Value;
            }
            if (worker.LastFailureAt.HasValue)
                data["lastFailureAt"] = worker.LastFailureAt.Value;

            if (_options.RequireReconciliation
                && (!worker.LastSuccessfulCycleAt.HasValue
                    || now - worker.LastSuccessfulCycleAt.Value
                        > heartbeatAgeLimit))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Tiến trình đối soát thanh toán chưa có tín hiệu hoạt động hợp lệ.",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                "Đối soát thanh toán đang hoạt động bình thường.",
                data));
        }
    }
}
