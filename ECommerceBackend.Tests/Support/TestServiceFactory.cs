using AutoMapper;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests.Support;

internal static class TestServiceFactory
{
    private static readonly Lazy<IMapper> Mapper = new(() =>
    {
        var config = new MapperConfiguration(
            cfg => cfg.AddProfile<MappingProfile>(),
            NullLoggerFactory.Instance);
        config.AssertConfigurationIsValid();
        return config.CreateMapper();
    });

    public static IMapper CreateMapper() => Mapper.Value;

    public static GenericRepository<T> Repository<T>(AppDbContext context)
        where T : class
        => new(context);

    public static EfDataConsistencyService Consistency(AppDbContext context)
        => new(context);

    public static ProductService CreateProductService(AppDbContext context, TimeProvider? timeProvider = null)
        => new(
            Repository<Product>(context),
            context,
            Consistency(context),
            CreateMapper(),
            timeProvider ?? TimeProvider.System);

    public static CategoryService CreateCategoryService(AppDbContext context)
        => new(
            Repository<Category>(context),
            context,
            Consistency(context),
            CreateMapper());

    public static UploadService CreateUploadService(
        AppDbContext context,
        TestWebHostEnvironment environment)
        => new(
            Repository<Product>(context),
            Repository<ProductImage>(context),
            context,
            Consistency(context),
            environment,
            CreateMapper(),
            Options.Create(new UploadOptions()),
            NullLogger<UploadService>.Instance);

    public static AuthService CreateAuthService(AppDbContext context, TimeProvider? timeProvider = null)
        => new(
            Repository<User>(context),
            Repository<Role>(context),
            Repository<UserRole>(context),
            Repository<Cart>(context),
            Repository<RefreshToken>(context),
            context,
            Consistency(context),
            Options.Create(new JwtOptions
            {
                Key = "phase-7-test-jwt-key-with-enough-length",
                Issuer = "ECommerceBackend.Tests",
                Audience = "ECommerceBackend.Tests.Client",
                AccessTokenMinutes = 60,
                RefreshTokenDays = 7
            }),
            timeProvider ?? TimeProvider.System);

    public static UserService CreateUserService(AppDbContext context, TimeProvider? timeProvider = null)
        => new(
            Repository<User>(context),
            Repository<Role>(context),
            Repository<UserRole>(context),
            context,
            Consistency(context),
            CreateMapper(),
            timeProvider ?? TimeProvider.System);

    public static OrderService CreateOrderService(
        AppDbContext context,
        TimeProvider? timeProvider = null,
        OrderLifecycleOptions? lifecycleOptions = null)
        => new(
            Repository<Order>(context),
            context,
            Consistency(context),
            new PaymentProviderResolver([new CashOnDeliveryPaymentProvider()]),
            new OutboxWriter(context),
            CreateMapper(),
            timeProvider ?? TimeProvider.System,
            Options.Create(lifecycleOptions ?? new OrderLifecycleOptions()));
}
