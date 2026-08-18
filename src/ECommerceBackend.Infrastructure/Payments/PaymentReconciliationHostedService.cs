using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class PaymentReconciliationHostedService : BackgroundService
    {
        public const string MeterName = "ECommerceBackend.PaymentReconciliation";

        private static readonly Meter Meter = new(MeterName);
        private static readonly Counter<long> ExaminedCounter =
            Meter.CreateCounter<long>("payments.reconciliation.examined");
        private static readonly Counter<long> UpdatedCounter =
            Meter.CreateCounter<long>("payments.reconciliation.updated");
        private static readonly Counter<long> FailedCounter =
            Meter.CreateCounter<long>("payments.reconciliation.failed");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly StripePaymentOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PaymentReconciliationHostedService> _logger;
        private readonly PaymentReconciliationWorkerStatus _status;

        public PaymentReconciliationHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<StripePaymentOptions> options,
            TimeProvider timeProvider,
            ILogger<PaymentReconciliationHostedService> logger,
            PaymentReconciliationWorkerStatus status)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _status = status;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            if (!_options.ReconciliationEnabled)
            {
                _logger.LogInformation(
                    "Payment reconciliation worker is disabled.");
                return;
            }

            _status.MarkStarted(_timeProvider.GetUtcNow().UtcDateTime);
            _logger.LogInformation(
                "Payment reconciliation worker started. BatchSize={BatchSize}, StaleAfterMinutes={StaleAfterMinutes}",
                _options.ReconciliationBatchSize,
                _options.ReconciliationStaleAfterMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var useCase = scope.ServiceProvider
                        .GetRequiredService<PaymentReconciliationUseCase>();
                    var result = await useCase.ExecuteBatchAsync(stoppingToken);
                    ExaminedCounter.Add(result.Examined);
                    UpdatedCounter.Add(result.Updated);
                    FailedCounter.Add(result.Failed);
                    _status.MarkSuccessfulCycle(
                        _timeProvider.GetUtcNow().UtcDateTime);

                    if (result.Examined > 0)
                    {
                        _logger.LogInformation(
                            "Payment reconciliation cycle completed. Examined={Examined}, Updated={Updated}, Failed={Failed}",
                            result.Examined,
                            result.Updated,
                            result.Failed);
                    }
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FailedCounter.Add(1);
                    _status.MarkFailure(
                        _timeProvider.GetUtcNow().UtcDateTime);
                    _logger.LogError(
                        ex,
                        "Payment reconciliation cycle failed.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            _options.ReconciliationPollIntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation(
                "Payment reconciliation worker stopped.");
        }
    }
}
