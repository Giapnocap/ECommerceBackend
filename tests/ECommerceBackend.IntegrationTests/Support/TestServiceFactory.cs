using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests.Support;

internal static class TestServiceFactory
{
    public static EfDataConsistencyService Consistency(AppDbContext context)
        => new(context);

    public static ProductService CreateProductService(AppDbContext context, TimeProvider? timeProvider = null)
        => new(
            new ProductRepository(context),
            new InventoryRepository(context),
            context,
            Consistency(context),
            timeProvider ?? TimeProvider.System);

    public static CategoryService CreateCategoryService(AppDbContext context)
        => new(
            new CategoryRepository(context),
            context,
            Consistency(context));

    public static CartService CreateCartService(AppDbContext context)
        => new(
            new CartRepository(context),
            new ProductRepository(context),
            context,
            Consistency(context));

    public static UploadService CreateUploadService(
        AppDbContext context,
        TestWebHostEnvironment environment)
        => new(
            new ProductRepository(context),
            context,
            Consistency(context),
            environment,
            Options.Create(new UploadOptions()),
            NullLogger<UploadService>.Instance);

    public static AuthService CreateAuthService(
        AppDbContext context,
        TimeProvider? timeProvider = null,
        AuthSecurityOptions? securityOptions = null,
        IPasswordHasher? passwordHasher = null,
        IAuditWriter? auditWriter = null,
        ISensitivePayloadProtector? payloadProtector = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        var protector = payloadProtector ?? new TestSensitivePayloadProtector();
        var jwtOptions = Options.Create(new JwtOptions
        {
            Key = "unit-test-jwt-signing-key-with-enough-length",
            Issuer = "ECommerceBackend.Tests",
            Audience = "ECommerceBackend.Tests.Client",
            AccessTokenMinutes = 60,
            RefreshTokenDays = 7
        });
        var options = Options.Create(
            securityOptions ?? new AuthSecurityOptions());
        var hasher = passwordHasher ?? new BCryptPasswordHasher();
        var audit = auditWriter ?? NullAuditWriter.Instance;
        var consistency = Consistency(context);
        var userRepository = new UserRepository(context);
        var cartRepository = new CartRepository(context);
        var authSessionRepository = new AuthSessionRepository(context);
        var tokenIssuer = new AuthTokenIssuer(jwtOptions);
        var outbox = new OutboxWriter(
            new OutboxRepository(context),
            clock,
            protector);
        return new AuthService(
            new AuthRegistrationUseCase(
                userRepository,
                cartRepository,
                authSessionRepository,
                context,
                consistency,
                hasher,
                tokenIssuer,
                clock),
            new AuthLoginUseCase(
                userRepository,
                authSessionRepository,
                context,
                consistency,
                tokenIssuer,
                options,
                hasher,
                audit,
                clock),
            new AuthRefreshUseCase(
                userRepository,
                authSessionRepository,
                context,
                consistency,
                tokenIssuer,
                clock),
            new AuthLogoutUseCase(
                authSessionRepository,
                context,
                consistency,
                clock),
            new AuthLogoutAllUseCase(
                authSessionRepository,
                context,
                consistency,
                clock),
            new PasswordResetUseCase(
                userRepository,
                authSessionRepository,
                context,
                consistency,
                hasher,
                outbox,
                audit,
                options,
                clock));
    }

    public static UserService CreateUserService(AppDbContext context, TimeProvider? timeProvider = null)
        => new(
            new UserRepository(context),
            new AuthSessionRepository(context),
            context,
            Consistency(context),
            timeProvider ?? TimeProvider.System);

    public static OrderService CreateOrderService(
        AppDbContext context,
        TimeProvider? timeProvider = null,
        OrderLifecycleOptions? lifecycleOptions = null,
        PricingOptions? pricingOptions = null,
        ReturnPolicyOptions? returnPolicyOptions = null)
    {
        var orderRepository = new OrderRepository(context);
        var paymentRepository = new PaymentRepository(context);
        var cartRepository = new CartRepository(context);
        var inventoryRepository = new InventoryRepository(context);
        var promotionRepository = new PromotionRepository(context);
        var fulfillmentRepository = new FulfillmentRepository(context);
        var queries = new OrderQueryUseCase(orderRepository);
        var consistency = Consistency(context);
        var providers = new PaymentProviderResolver(
            [new CashOnDeliveryPaymentProvider()]);
        var outbox = new OutboxWriter(new OutboxRepository(context));
        var clock = timeProvider ?? TimeProvider.System;
        var options = Options.Create(
            lifecycleOptions ?? new OrderLifecycleOptions());
        var pricing = new OrderPricingUseCase(
            cartRepository,
            promotionRepository,
            clock,
            Options.Create(
                pricingOptions ?? new PricingOptions()));
        var checkoutCartLoader = new CheckoutCartLoader(
            cartRepository,
            consistency);
        var checkoutOrderFactory = new CheckoutOrderFactory(
            providers,
            options);
        var checkoutOrderWriter = new CheckoutOrderWriter(
            orderRepository,
            paymentRepository,
            cartRepository,
            inventoryRepository);
        var checkout = new OrderCheckoutUseCase(
            orderRepository,
            context,
            consistency,
            outbox,
            checkoutCartLoader,
            checkoutOrderFactory,
            checkoutOrderWriter,
            pricing,
            queries,
            clock,
            options);
        var cancellation = new OrderCancellationWorkflow(
            orderRepository,
            paymentRepository,
            inventoryRepository,
            consistency,
            outbox);
        var statusUpdate = new OrderStatusUpdateUseCase(
            context,
            consistency,
            outbox,
            NullAuditWriter.Instance,
            cancellation,
            queries,
            clock);
        var customerCancellation =
            new CustomerOrderCancellationUseCase(
                context,
                consistency,
                cancellation,
                queries,
                clock);
        var expiration = new PendingOrderExpirationUseCase(
            orderRepository,
            context,
            consistency,
            cancellation);
        var refund = new OrderRefundUseCase(
            paymentRepository,
            fulfillmentRepository,
            orderRepository,
            context,
            consistency,
            outbox,
            queries,
            clock);
        var fulfillmentWorkflow = new OrderFulfillmentWorkflow(
            orderRepository,
            paymentRepository,
            consistency,
            queries);
        var shipmentDispatch = new ShipmentDispatchUseCase(
            fulfillmentRepository,
            context,
            consistency,
            outbox,
            NullAuditWriter.Instance,
            fulfillmentWorkflow,
            clock);
        var shipmentDelivery = new ShipmentDeliveryUseCase(
            fulfillmentRepository,
            context,
            consistency,
            outbox,
            NullAuditWriter.Instance,
            fulfillmentWorkflow,
            clock);
        var returnWorkflow = new OrderReturnWorkflow(
            orderRepository,
            inventoryRepository,
            consistency,
            NullAuditWriter.Instance,
            queries);
        var returnRequest = new OrderReturnRequestUseCase(
            fulfillmentRepository,
            context,
            consistency,
            outbox,
            returnWorkflow,
            clock,
            Options.Create(
                returnPolicyOptions ?? new ReturnPolicyOptions()));
        var returnReview = new OrderReturnReviewUseCase(
            fulfillmentRepository,
            context,
            consistency,
            outbox,
            returnWorkflow,
            clock);
        var returnReceipt = new OrderReturnReceiptUseCase(
            fulfillmentRepository,
            context,
            consistency,
            outbox,
            returnWorkflow,
            clock);
        return new OrderService(
            checkout,
            statusUpdate,
            customerCancellation,
            expiration,
            refund,
            shipmentDispatch,
            shipmentDelivery,
            returnRequest,
            returnReview,
            returnReceipt,
            queries,
            pricing);
    }

    public static PromotionService CreatePromotionService(
        AppDbContext context,
        TimeProvider? timeProvider = null)
        => new(
            new PromotionRepository(context),
            context,
            Consistency(context),
            NullAuditWriter.Instance,
            timeProvider ?? TimeProvider.System);
}

internal sealed class TestSensitivePayloadProtector : ISensitivePayloadProtector
{
    private const string Prefix = "test-protected:";

    public string Protect(string plaintext)
        => Prefix + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string protectedPayload)
    {
        if (!protectedPayload.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Protected test payload is invalid.");

        return System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(protectedPayload[Prefix.Length..]));
    }
}
