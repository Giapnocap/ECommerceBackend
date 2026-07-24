using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Orders
{
    public sealed class OrderExpirationHostedService : BackgroundService
    {
        private static readonly Meter Meter = new("ECommerceBackend.OrderExpiration");
        private static readonly Counter<long> ExpiredCounter = Meter.CreateCounter<long>("orders.expired");
        private static readonly Counter<long> FailedCounter = Meter.CreateCounter<long>("orders.expiration.failed");

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrderLifecycleOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<OrderExpirationHostedService> _logger;
        private readonly OrderExpirationWorkerStatus _status;

        public OrderExpirationHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<OrderLifecycleOptions> options,
            TimeProvider timeProvider,
            ILogger<OrderExpirationHostedService> logger,
            OrderExpirationWorkerStatus status)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _status = status;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.ExpirationEnabled)
            {
                _logger.LogInformation("Order expiration worker is disabled.");
                return;
            }

            _status.MarkStarted(_timeProvider.GetUtcNow().UtcDateTime);
            _logger.LogInformation(
                "Order expiration worker started. DryRun={DryRun} BatchSize={BatchSize}",
                _options.ExpirationDryRun,
                _options.ExpirationBatchSize);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var handled = await ProcessBatchAsync(stoppingToken);
                    _status.MarkSuccessfulCycle(_timeProvider.GetUtcNow().UtcDateTime);
                    if (handled > 0 && !_options.ExpirationDryRun)
                        continue;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _status.MarkFailure(_timeProvider.GetUtcNow().UtcDateTime);
                    FailedCounter.Add(1);
                    _logger.LogError(ex, "Order expiration cycle failed.");
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(_options.ExpirationPollIntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("Order expiration worker stopped.");
        }

        private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var asOf = _timeProvider.GetUtcNow().UtcDateTime;
            var orderIds = await orderService.GetDuePendingOrderIdsAsync(
                asOf,
                _options.ExpirationBatchSize,
                cancellationToken);

            if (_options.ExpirationDryRun)
            {
                if (orderIds.Count > 0)
                {
                    _logger.LogWarning(
                        "Order expiration dry-run found {OrderCount} overdue pending orders.",
                        orderIds.Count);
                }

                return orderIds.Count;
            }

            var expiredCount = 0;
            foreach (var orderId in orderIds)
            {
                try
                {
                    if (await orderService.ExpirePendingOrderAsync(orderId, asOf, cancellationToken))
                    {
                        expiredCount++;
                        ExpiredCounter.Add(1);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    FailedCounter.Add(1);
                    _logger.LogError(ex, "Failed to expire order {OrderId}.", orderId);
                }
            }

            return expiredCount;
        }
    }
}
