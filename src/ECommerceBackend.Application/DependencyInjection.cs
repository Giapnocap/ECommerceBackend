using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerceBackend.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddECommerceApplication(
            this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddHttpContextAccessor();

            AddIdentityFeature(services);
            AddCatalogFeature(services);
            AddOrderingFeature(services);
            AddOperationsFeature(services);

            return services;
        }

        private static void AddIdentityFeature(IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<AuthRegistrationUseCase>();
            services.AddScoped<AuthSessionService>();
            services.AddScoped<PasswordResetUseCase>();
            services.AddScoped<AuthTokenIssuer>();
            services.AddScoped<IUserService, UserService>();
        }

        private static void AddCatalogFeature(IServiceCollection services)
        {
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IUploadService, UploadService>();
            services.AddScoped<IUploadReconciliationService, UploadReconciliationService>();
            services.AddScoped<IPromotionService, PromotionService>();
        }

        private static void AddOrderingFeature(IServiceCollection services)
        {
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
            services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
        }

        private static void AddOperationsFeature(IServiceCollection services)
        {
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<IOutboxWriter, OutboxWriter>();
            services.AddScoped<IAuditWriter, AuditWriter>();
            services.AddScoped<IOperationsService, OperationsService>();
            services.AddScoped<DeadLetterUseCase>();
            services.AddScoped<AuditQueryUseCase>();
            services.AddScoped<DataRetentionUseCase>();
            services.AddScoped<IOutboxMessageHandler, NotificationOutboxMessageHandler>();
        }
    }
}
