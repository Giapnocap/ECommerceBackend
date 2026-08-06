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
            services.AddScoped<AuthLoginUseCase>();
            services.AddScoped<AuthRefreshUseCase>();
            services.AddScoped<AuthLogoutUseCase>();
            services.AddScoped<AuthLogoutAllUseCase>();
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
            services.AddScoped<CheckoutCartLoader>();
            services.AddScoped<CheckoutOrderFactory>();
            services.AddScoped<CheckoutOrderWriter>();
            services.AddScoped<OrderCheckoutUseCase>();
            services.AddScoped<OrderPricingUseCase>();
            services.AddScoped<OrderCancellationWorkflow>();
            services.AddScoped<OrderStatusUpdateUseCase>();
            services.AddScoped<CustomerOrderCancellationUseCase>();
            services.AddScoped<PendingOrderExpirationUseCase>();
            services.AddScoped<OrderFulfillmentWorkflow>();
            services.AddScoped<ShipmentDispatchUseCase>();
            services.AddScoped<ShipmentDeliveryUseCase>();
            services.AddScoped<OrderReturnWorkflow>();
            services.AddScoped<OrderReturnRequestUseCase>();
            services.AddScoped<OrderReturnReviewUseCase>();
            services.AddScoped<OrderReturnReceiptUseCase>();
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
