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
        var outbox = new OutboxWriter(context, clock, protector);
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
            new AuthSessionService(
                userRepository,
                authSessionRepository,
                context,
                consistency,
                tokenIssuer,
                options,
                hasher,
                audit,
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
        OrderLifecycleOptions? lifecycleOptions = null)
    {
        var orderRepository = new OrderRepository(context);
        var paymentRepository = new PaymentRepository(context);
        var cartRepository = new CartRepository(context);
        var inventoryRepository = new InventoryRepository(context);
        var queries = new OrderQueryUseCase(orderRepository);
        var consistency = Consistency(context);
        var providers = new PaymentProviderResolver(
            [new CashOnDeliveryPaymentProvider()]);
        var outbox = new OutboxWriter(context);
        var clock = timeProvider ?? TimeProvider.System;
        var options = Options.Create(
            lifecycleOptions ?? new OrderLifecycleOptions());
        var checkout = new OrderCheckoutUseCase(
            orderRepository,
            paymentRepository,
            cartRepository,
            inventoryRepository,
            context,
            consistency,
            providers,
            outbox,
            queries,
            clock,
            options);
        var commands = new OrderCommandService(
            orderRepository,
            paymentRepository,
            cartRepository,
            inventoryRepository,
            context,
            consistency,
            providers,
            outbox,
            queries,
            clock,
            options);
        var refund = new OrderRefundUseCase(
            paymentRepository,
            context,
            consistency,
            outbox,
            queries,
            clock);
        return new OrderService(checkout, commands, refund, queries);
    }
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
