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
            AddPaymentAndNotificationAdapters(services);
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
            services.AddScoped<IDataRetentionRepository, DataRetentionRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IFulfillmentRepository, FulfillmentRepository>();
            services.AddScoped<IOrderRepository, OrderRepository>();
            services.AddScoped<IOutboxRepository, OutboxRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<IPromotionRepository, PromotionRepository>();
            services.AddScoped<IProductRepository, ProductRepository>();
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

        private static void AddPaymentAndNotificationAdapters(IServiceCollection services)
        {
            services.AddScoped<INotificationSender, ConfigurableNotificationSender>();
            services.AddScoped<OutboxProcessor>();
            services.AddSingleton<IPaymentProvider, CashOnDeliveryPaymentProvider>();
            services.AddSingleton<IPaymentProvider, GenericHmacPaymentProvider>();
            services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();
        }

        private static void AddHostedWorkers(IServiceCollection services)
        {
            services.AddSingleton<OrderExpirationWorkerStatus>();
            services.AddSingleton<OutboxWorkerStatus>();
            services.AddSingleton<DataRetentionWorkerStatus>();
            services.AddScoped<AdminBootstrapper>();
            services.AddHostedService<AdminBootstrapHostedService>();
            services.AddHostedService<OutboxDispatcherHostedService>();
            services.AddHostedService<OrderExpirationHostedService>();
            services.AddHostedService<DataRetentionHostedService>();
        }
    }
}
