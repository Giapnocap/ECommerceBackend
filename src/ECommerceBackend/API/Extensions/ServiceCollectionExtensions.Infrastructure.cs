using System.Net;
using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Maintenance;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Observability;
using ECommerceBackend.Infrastructure.Orders;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddECommerceReverseProxy(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var proxyOptions = configuration
                .GetSection(ReverseProxyOptions.SectionName)
                .Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = proxyOptions.Enabled
                    ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                    : ForwardedHeaders.None;
                options.ForwardLimit = proxyOptions.ForwardLimit;
                options.RequireHeaderSymmetry = proxyOptions.RequireHeaderSymmetry;
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();

                foreach (var proxy in proxyOptions.KnownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var address))
                        options.KnownProxies.Add(address);
                }

                foreach (var network in proxyOptions.KnownNetworks)
                {
                    if (TryParseNetwork(network, out var parsedNetwork))
                        options.KnownNetworks.Add(parsedNetwork);
                }
            });

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
            services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<AppDbContext>());
            services.AddScoped<IDataConsistencyService, EfDataConsistencyService>();

            return services;
        }

        public static IServiceCollection AddECommerceRepositoriesAndServices(this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddHttpContextAccessor();
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
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<AuthRegistrationUseCase>();
            services.AddScoped<AuthSessionService>();
            services.AddScoped<PasswordResetUseCase>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IUploadService, UploadService>();
            services.AddScoped<ICartService, CartService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<OrderCheckoutUseCase>();
            services.AddScoped<OrderPricingUseCase>();
            services.AddScoped<OrderCommandService>();
            services.AddScoped<OrderFulfillmentUseCase>();
            services.AddScoped<OrderReturnUseCase>();
            services.AddScoped<OrderRefundUseCase>();
            services.AddScoped<OrderQueryUseCase>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<IPromotionService, PromotionService>();
            services.AddScoped<AuthTokenIssuer>();
            services.AddScoped<IOutboxWriter, OutboxWriter>();
            services.AddScoped<IAuditWriter, AuditWriter>();
            services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
            services.AddSingleton<
                ISensitivePayloadProtector,
                DataProtectionSensitivePayloadProtector>();
            services.AddScoped<IOperationsService, OperationsService>();
            services.AddScoped<DeadLetterUseCase>();
            services.AddScoped<AuditQueryUseCase>();
            services.AddScoped<DataRetentionUseCase>();
            services.AddScoped<IUploadReconciliationService, UploadReconciliationService>();
            services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
            services.AddScoped<IOutboxStore, EfOutboxStore>();
            services.AddScoped<IOutboxMessageHandler, NotificationOutboxMessageHandler>();
            services.AddScoped<INotificationSender, ConfigurableNotificationSender>();
            services.AddScoped<OutboxProcessor>();
            services.AddSingleton<IPaymentProvider, CashOnDeliveryPaymentProvider>();
            services.AddSingleton<IPaymentProvider, GenericHmacPaymentProvider>();
            services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();
            services.AddSingleton<OrderExpirationWorkerStatus>();
            services.AddSingleton<OutboxWorkerStatus>();
            services.AddSingleton<DataRetentionWorkerStatus>();
            services.AddScoped<AdminBootstrapper>();
            services.AddHostedService<AdminBootstrapHostedService>();
            services.AddHostedService<OutboxDispatcherHostedService>();
            services.AddHostedService<OrderExpirationHostedService>();
            services.AddHostedService<DataRetentionHostedService>();

            return services;
        }

        public static IServiceCollection AddECommerceHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy("Application is running."), tags: ["live"])
                .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
                .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"])
                .AddCheck<OrderExpirationHealthCheck>("order-expiration", tags: ["ready"])
                .AddCheck<DataRetentionHealthCheck>("data-retention", tags: ["ready"]);

            return services;
        }
    }
}
