using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Maintenance;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Observability;
using ECommerceBackend.Infrastructure.Orders;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Pricing;
using ECommerceBackend.Infrastructure.Security;
using ECommerceBackend.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ECommerceBackend.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddECommerceInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddECommerceDatabase(configuration);

            AddRepositories(services);
            AddSecurityAdapters(services);
            AddStorageAdapters(services);
            AddPricingAdapters(services, configuration);
            AddPaymentAndNotificationAdapters(services, configuration);
            AddHostedWorkers(services);

            return services;
        }

        public static IServiceCollection AddECommerceDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var databaseOptions = configuration
                .GetSection(DatabaseOptions.SectionName)
                .Get<DatabaseOptions>() ?? new DatabaseOptions();

            services.TryAddSingleton<DatabaseTelemetryInterceptor>();
            services.AddDbContext<AppDbContext>((provider, options) =>
            {
                options.UseSqlServer(
                    configuration.GetConnectionString("Default"),
                    sqlServer => sqlServer.CommandTimeout(databaseOptions.CommandTimeoutSeconds));
                options.AddInterceptors(
                    provider.GetRequiredService<DatabaseTelemetryInterceptor>());
            });
            services.AddScoped<IUnitOfWork>(
                provider => provider.GetRequiredService<AppDbContext>());
            services.AddScoped<IDataConsistencyService, EfDataConsistencyService>();

            return services;
        }

        private static void AddRepositories(IServiceCollection services)
        {
            services.AddScoped<IAuditRepository, AuditRepository>();
            services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
            services.AddScoped<ICartRepository, CartRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<ICustomerManagementReadRepository, CustomerManagementReadRepository>();
            services.AddScoped<IDataRetentionRepository, DataRetentionRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IFulfillmentRepository, FulfillmentRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<IPromotionRepository, PromotionRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
            services.AddScoped<IAdminDashboardReadRepository, AdminDashboardReadRepository>();
            services.AddScoped<IReportReadRepository, ReportReadRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IOutboxStore, EfOutboxStore>();
        }

        private static void AddSecurityAdapters(IServiceCollection services)
        {
            services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
            services.AddSingleton<IAccessTokenGenerator, JwtAccessTokenGenerator>();
            services.AddSingleton<
                ISensitivePayloadProtector,
                DataProtectionSensitivePayloadProtector>();
        }

        private static void AddStorageAdapters(IServiceCollection services)
        {
            services.AddSingleton<IProductImageStorage, LocalProductImageStorage>();
            services.AddSingleton<IProductImageStorageHealthProbe>(provider =>
                (IProductImageStorageHealthProbe)provider
                    .GetRequiredService<IProductImageStorage>());
        }

        private static void AddPricingAdapters(
            IServiceCollection services,
            IConfiguration configuration)
        {
            services.TryAddSingleton(TimeProvider.System);
            var exchangeRateOptions = configuration
                .GetSection(ExchangeRateOptions.SectionName)
                .Get<ExchangeRateOptions>() ?? new ExchangeRateOptions();
            services.Configure<ExchangeRateOptions>(
                configuration.GetSection(ExchangeRateOptions.SectionName));
            services.AddMemoryCache();
            services.AddHttpClient(
                CurrencyApiExchangeRateProvider.HttpClientName,
                client =>
                {
                    client.BaseAddress = Uri.TryCreate(
                        exchangeRateOptions.BaseUrl,
                        UriKind.Absolute,
                        out var baseUri)
                            ? baseUri
                            : new Uri("https://api.currencyapi.com/");
                    client.Timeout = TimeSpan.FromSeconds(
                        exchangeRateOptions.RequestTimeoutSeconds);
                });
            services.AddSingleton<
                IExchangeRateProvider,
                CurrencyApiExchangeRateProvider>();
        }

        private static void AddPaymentAndNotificationAdapters(
            IServiceCollection services,
            IConfiguration configuration)
        {
            var stripeOptions = configuration
                .GetSection(StripePaymentOptions.SectionName)
                .Get<StripePaymentOptions>() ?? new StripePaymentOptions();
            services.Configure<StripePaymentOptions>(
                configuration.GetSection(StripePaymentOptions.SectionName));
            services.AddScoped<INotificationSender, ConfigurableNotificationSender>();
            services.AddScoped<OutboxProcessor>();
            services.AddSingleton<IPaymentProvider, CashOnDeliveryPaymentProvider>();
            services.AddSingleton<IPaymentProvider, GenericHmacPaymentProvider>();
            if (stripeOptions.Enabled)
            {
                services.AddSingleton<
                    IPaymentProvider,
                    StripeCheckoutPaymentProvider>();
            }
            services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();
            services.AddHttpClient<IPaymentGateway, StripePaymentGateway>(client =>
            {
                client.BaseAddress = Uri.TryCreate(
                    stripeOptions.BaseUrl,
                    UriKind.Absolute,
                    out var baseUri)
                        ? baseUri
                        : new Uri("https://api.stripe.com/");
                client.Timeout = TimeSpan.FromSeconds(
                    stripeOptions.RequestTimeoutSeconds);
            });
        }

        private static void AddHostedWorkers(IServiceCollection services)
        {
            services.AddSingleton<OrderExpirationWorkerStatus>();
            services.AddSingleton<OutboxWorkerStatus>();
            services.AddSingleton<DataRetentionWorkerStatus>();
            services.AddSingleton<PaymentReconciliationWorkerStatus>();
            services.AddScoped<AdminBootstrapper>();
            services.AddHostedService<AdminBootstrapHostedService>();
            services.AddHostedService<OutboxDispatcherHostedService>();
            services.AddHostedService<OrderExpirationHostedService>();
            services.AddHostedService<PaymentReconciliationHostedService>();
            services.AddHostedService<DataRetentionHostedService>();
        }
    }
}
