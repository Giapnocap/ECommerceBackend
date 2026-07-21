namespace ECommerceBackend.Infrastructure.Data
{
    public sealed class AdminBootstrapHostedService : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public AdminBootstrapHostedService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<AdminBootstrapper>();
            await bootstrapper.InitializeAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
