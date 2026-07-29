using ECommerceBackend.Application;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Infrastructure;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Maintenance;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Orders;
using ECommerceBackend.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ECommerceBackend.Tests;

public sealed class DependencyRegistrationTests
{
    [Fact]
    public void ApplicationModule_RegistersFeatureServicesWithExpectedLifetimes()
    {
        var services = new ServiceCollection();

        services.AddECommerceApplication();

        AssertRegistration<IAuthService, AuthService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IUserService, UserService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IProductService, ProductService>(services, ServiceLifetime.Scoped);
        AssertRegistration<ICartService, CartService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IOrderService, OrderService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IInventoryService, InventoryService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IReportService, ReportService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IOperationsService, OperationsService>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IOutboxMessageHandler, NotificationOutboxMessageHandler>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<OrderCheckoutUseCase, OrderCheckoutUseCase>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<AuthLoginUseCase, AuthLoginUseCase>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<AuthRefreshUseCase, AuthRefreshUseCase>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<OrderStatusUpdateUseCase, OrderStatusUpdateUseCase>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<ShipmentDispatchUseCase, ShipmentDispatchUseCase>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<OrderReturnRequestUseCase, OrderReturnRequestUseCase>(
            services,
            ServiceLifetime.Scoped);

        var timeProvider = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Equal(ServiceLifetime.Singleton, timeProvider.Lifetime);
        Assert.Same(TimeProvider.System, timeProvider.ImplementationInstance);
    }

    [Fact]
    public void InfrastructureModule_RegistersPersistenceAdaptersAndWorkers()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Server=localhost;Database=ECommerceIntegration;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddECommerceInfrastructure(configuration);

        AssertRegistration<IAuthSessionRepository, AuthSessionRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IProductRepository, ProductRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IOrderRepository, OrderRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IOutboxRepository, OutboxRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<INotificationSender, ConfigurableNotificationSender>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IPaymentProviderResolver, PaymentProviderResolver>(
            services,
            ServiceLifetime.Singleton);

        var paymentProviders = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPaymentProvider))
            .ToArray();
        Assert.Equal(2, paymentProviders.Length);
        Assert.All(
            paymentProviders,
            descriptor => Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime));

        AssertHostedService<AdminBootstrapHostedService>(services);
        AssertHostedService<OutboxDispatcherHostedService>(services);
        AssertHostedService<OrderExpirationHostedService>(services);
        AssertHostedService<DataRetentionHostedService>(services);
    }

    private static void AssertRegistration<TService, TImplementation>(
        IServiceCollection services,
        ServiceLifetime lifetime)
    {
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(TService));

        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(lifetime, descriptor.Lifetime);
    }

    private static void AssertHostedService<THostedService>(IServiceCollection services)
        where THostedService : class, IHostedService
    {
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(THostedService)
                && descriptor.Lifetime == ServiceLifetime.Singleton);
    }
}
