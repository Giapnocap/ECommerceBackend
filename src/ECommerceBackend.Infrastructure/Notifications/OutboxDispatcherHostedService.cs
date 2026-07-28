using ECommerceBackend.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Infrastructure.Notifications
{
    public sealed class OutboxDispatcherHostedService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OutboxOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<OutboxDispatcherHostedService> _logger;
        private readonly OutboxWorkerStatus _status;

        public OutboxDispatcherHostedService(
            IServiceScopeFactory scopeFactory,
            IOptions<OutboxOptions> options,
            TimeProvider timeProvider,
            ILogger<OutboxDispatcherHostedService> logger,
            OutboxWorkerStatus status)
        {
            _scopeFactory = scopeFactory;
            _options = options.Value;
            _timeProvider = timeProvider;
            _logger = logger;
            _status = status;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.Enabled)
            {
                _logger.LogInformation("Outbox dispatcher is disabled.");
                return;
            }

            _status.MarkStarted(_timeProvider.GetUtcNow().UtcDateTime);
            _logger.LogInformation("Outbox dispatcher started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                    var handled = await processor.ProcessBatchAsync(stoppingToken);
                    _status.MarkSuccessfulCycle(_timeProvider.GetUtcNow().UtcDateTime);
                    if (handled > 0)
                        continue;

                    await Task.Delay(
                        TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                        stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _status.MarkFailure(_timeProvider.GetUtcNow().UtcDateTime);
                    _logger.LogError(ex, "Outbox dispatcher cycle failed.");

                    try
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(_options.PollIntervalSeconds),
                            stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            _logger.LogInformation("Outbox dispatcher stopped.");
        }
    }
}
