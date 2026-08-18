using ECommerceBackend.Application;
using ECommerceBackend.API.Extensions;
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
using ECommerceBackend.Infrastructure.Pricing;
using ECommerceBackend.Infrastructure.Security;
using ECommerceBackend.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class DependencyRegistrationTests
{
    [Fact]
    public async Task ApiHealthChecks_ApplyConfiguredTimeoutToReadinessDependencies()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HealthChecks:DependencyTimeoutSeconds"] = "7"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddECommerceHealthChecks(configuration);

        using var provider = services.BuildServiceProvider();
        var registrations = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations;
        var selfRegistration = Assert.Single(
            registrations,
            registration => registration.Name == "self");
        var selfResult = await selfRegistration
            .Factory(provider)
            .CheckHealthAsync(new HealthCheckContext());
        var readinessRegistrations = registrations
            .Where(registration => registration.Tags.Contains("ready"))
            .ToArray();

        Assert.Equal("Ứng dụng đang hoạt động.", selfResult.Description);
        Assert.Equal(6, readinessRegistrations.Length);
        Assert.All(
            readinessRegistrations,
            registration => Assert.Equal(
                TimeSpan.FromSeconds(7),
                registration.Timeout));
    }

    [Fact]
    public void ApplicationModule_RegistersFeatureServicesWithExpectedLifetimes()
    {
        var services = new ServiceCollection();

        services.AddECommerceApplication();

        AssertRegistration<IAuthService, AuthService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IUserService, UserService>(services, ServiceLifetime.Scoped);
        AssertRegistration<ICustomerManagementService, CustomerManagementService>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IProductService, ProductService>(services, ServiceLifetime.Scoped);
        AssertRegistration<ICartService, CartService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IOrderService, OrderService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IInventoryService, InventoryService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IReportService, ReportService>(services, ServiceLifetime.Scoped);
        AssertRegistration<IAdminDashboardService, AdminDashboardService>(
            services,
            ServiceLifetime.Scoped);
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
        AssertRegistration<PaymentReconciliationUseCase, PaymentReconciliationUseCase>(
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
        AssertRegistration<ICustomerManagementReadRepository, CustomerManagementReadRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IOrderRepository, OrderRepository>(
            services,
            ServiceLifetime.Scoped);
        AssertRegistration<IAdminDashboardReadRepository, AdminDashboardReadRepository>(
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
        AssertRegistration<IExchangeRateProvider, CurrencyApiExchangeRateProvider>(
            services,
            ServiceLifetime.Singleton);
        AssertRegistration<IPasswordHasher, BCryptPasswordHasher>(
            services,
            ServiceLifetime.Singleton);
        AssertRegistration<IAccessTokenGenerator, JwtAccessTokenGenerator>(
            services,
            ServiceLifetime.Singleton);
        AssertRegistration<IProductImageStorage, LocalProductImageStorage>(
            services,
            ServiceLifetime.Singleton);
        var storageHealthProbe = Assert.Single(
            services,
            descriptor => descriptor.ServiceType
                == typeof(IProductImageStorageHealthProbe));
        Assert.Equal(ServiceLifetime.Singleton, storageHealthProbe.Lifetime);
        Assert.NotNull(storageHealthProbe.ImplementationFactory);

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
        AssertHostedService<PaymentReconciliationHostedService>(services);
        AssertHostedService<DataRetentionHostedService>(services);
    }

    [Fact]
    public void InfrastructureModule_WithStripeEnabled_ExposesCardCapability()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Server=localhost;Database=ECommerceIntegration;Trusted_Connection=True;TrustServerCertificate=True;",
                ["Payments:Stripe:Enabled"] = "true",
                ["Payments:Stripe:SecretKey"] =
                    "sk_test_dependency_secret_123456",
                ["Payments:Stripe:PublishableKey"] =
                    "pk_test_dependency_public_123456",
                ["Payments:Stripe:WebhookSecret"] =
                    "whsec_dependency_webhook_123456"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddECommerceInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();

        var resolver = provider
            .GetRequiredService<IPaymentProviderResolver>();
        var card = Assert.Single(
            resolver.GetCheckoutCapabilities(),
            capability => capability.Method
                == Domain.Enums.PaymentMethod.Card);

        Assert.Equal("stripe", card.ProviderCode);
        Assert.True(card.SupportsWebhooks);
        Assert.True(card.RequiresExternalInitialization);
        Assert.NotNull(provider.GetRequiredService<IPaymentGateway>());
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
