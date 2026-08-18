using ECommerceBackend.API.Controllers;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Infrastructure.Data;

namespace ECommerceBackend.Tests;

public sealed class ArchitectureBoundaryTests
{
    [Fact]
    public void ApplicationSources_AreGroupedByBusinessCapability()
    {
        var applicationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ECommerceBackend.Application");
        var featuresDirectory = Path.Combine(applicationDirectory, "Features");
        var expectedFeatures = new[]
        {
            "Auth",
            "Carts",
            "Catalog",
            "Dashboard",
            "Inventory",
            "Notifications",
            "Operations",
            "Orders",
            "Payments",
            "Pricing",
            "Promotions",
            "Reports",
            "Users"
        };
        var actualFeatures = Directory
            .EnumerateDirectories(featuresDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedFeatures, actualFeatures);
        Assert.All(
            actualFeatures,
            feature => Assert.NotEmpty(Directory.EnumerateFiles(
                Path.Combine(featuresDirectory, feature!),
                "*.cs",
                SearchOption.TopDirectoryOnly)));

        var retiredTechnicalDirectories = new[]
        {
            Path.Combine(applicationDirectory, "DTOs"),
            Path.Combine(applicationDirectory, "Services"),
            Path.Combine(applicationDirectory, "Validation"),
            Path.Combine(applicationDirectory, "Interfaces", "Repositories")
        };
        Assert.All(
            retiredTechnicalDirectories,
            directory => Assert.False(
                Directory.Exists(directory),
                $"Feature-owned source returned to retired directory '{directory}'."));
    }

    [Fact]
    public void PublicFacades_DoNotOwnPersistenceOrInfrastructureDependencies()
    {
        var facadeTypes = new[]
        {
            typeof(AuthService),
            typeof(OrderService),
            typeof(OperationsService)
        };
        var forbiddenTypes = new[]
        {
            typeof(IUnitOfWork),
            typeof(IDataConsistencyService)
        };

        foreach (var facadeType in facadeTypes)
        {
            var dependencies = facadeType
                .GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Select(parameter => parameter.ParameterType)
                .Concat(
                    facadeType
                        .GetFields(
                            System.Reflection.BindingFlags.Instance
                            | System.Reflection.BindingFlags.NonPublic)
                        .Select(field => field.FieldType))
                .ToArray();

            Assert.DoesNotContain(
                dependencies,
                dependency => forbiddenTypes.Contains(dependency));
            Assert.All(
                dependencies,
                dependency => Assert.Equal(
                    typeof(AuthService).Assembly,
                    dependency.Assembly));
        }
    }

    [Fact]
    public void UseCaseServices_HaveFocusedPublicSurface()
    {
        AssertPublicMethodCount<AuthRegistrationUseCase>(1);
        AssertPublicMethodCount<AuthLoginUseCase>(1);
        AssertPublicMethodCount<AuthRefreshUseCase>(1);
        AssertPublicMethodCount<AuthLogoutUseCase>(1);
        AssertPublicMethodCount<AuthLogoutAllUseCase>(1);
        AssertPublicMethodCount<PasswordResetUseCase>(2);
        AssertPublicMethodCount<OrderQueryUseCase>(5);
        AssertPublicMethodCount<OrderCheckoutUseCase>(1);
        AssertPublicMethodCount<OrderPricingUseCase>(1);
        AssertPublicMethodCount<OrderStatusUpdateUseCase>(1);
        AssertPublicMethodCount<CustomerOrderCancellationUseCase>(1);
        AssertPublicMethodCount<PendingOrderExpirationUseCase>(2);
        AssertPublicMethodCount<ShipmentDispatchUseCase>(1);
        AssertPublicMethodCount<ShipmentDeliveryUseCase>(1);
        AssertPublicMethodCount<OrderReturnRequestUseCase>(1);
        AssertPublicMethodCount<OrderReturnReviewUseCase>(1);
        AssertPublicMethodCount<OrderReturnReceiptUseCase>(1);
        AssertPublicMethodCount<OrderRefundUseCase>(1);
        AssertPublicMethodCount<DeadLetterUseCase>(2);
        AssertPublicMethodCount<AuditQueryUseCase>(2);
        AssertPublicMethodCount<DataRetentionUseCase>(1);
    }

    [Fact]
    public void LegacyMultiCommandServices_AreNotPartOfApplicationAssembly()
    {
        var legacyTypeNames = new[]
        {
            "AuthSessionService",
            "OrderCommandService",
            "OrderFulfillmentUseCase",
            "OrderReturnUseCase"
        };
        var applicationTypes = typeof(AuthService).Assembly.GetTypes();

        Assert.DoesNotContain(
            applicationTypes,
            type => legacyTypeNames.Contains(
                type.Name,
                StringComparer.Ordinal));
    }

    [Fact]
    public void RemovedGenericRepository_IsNotPartOfApplicationOrInfrastructure()
    {
        var productionTypes = new[]
            {
                typeof(AuthService).Assembly,
                typeof(AppDbContext).Assembly
            }
            .Distinct()
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();

        Assert.DoesNotContain(
            productionTypes,
            type => type.Name.Contains(
                "GenericRepository",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            productionTypes
                .SelectMany(type => type.GetConstructors())
                .SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType.Name.Contains(
                "GenericRepository",
                StringComparison.Ordinal));
    }

    [Fact]
    public void LayerAssemblies_EnforceDependencyDirection()
    {
        var domainAssembly = typeof(ECommerceBackend.Domain.Entities.User).Assembly;
        var applicationAssembly = typeof(AuthService).Assembly;
        var infrastructureAssembly = typeof(AppDbContext).Assembly;

        var domainReferences = ReferencedAssemblyNames(domainAssembly);
        var applicationReferences = ReferencedAssemblyNames(applicationAssembly);
        var infrastructureReferences = ReferencedAssemblyNames(infrastructureAssembly);

        Assert.DoesNotContain(
            domainReferences,
            name => name.StartsWith("ECommerceBackend.", StringComparison.Ordinal));
        Assert.Contains(domainAssembly.GetName().Name!, applicationReferences);
        Assert.DoesNotContain(infrastructureAssembly.GetName().Name!, applicationReferences);
        Assert.DoesNotContain(
            applicationReferences,
            name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.Contains(applicationAssembly.GetName().Name!, infrastructureReferences);
        Assert.Contains(domainAssembly.GetName().Name!, infrastructureReferences);
    }

    [Fact]
    public void ApplicationServices_DoNotDependOnAppDbContext()
    {
        var serviceTypes = typeof(AuthService).Assembly
            .GetTypes()
            .Where(type => type.IsClass
                && type.Namespace == typeof(AuthService).Namespace);
        var dependencies = serviceTypes
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Concat(
                serviceTypes.SelectMany(type => type.GetFields(
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic))
                    .Select(field => field.FieldType));

        Assert.DoesNotContain(typeof(AppDbContext), dependencies);
    }

    [Fact]
    public void ApplicationSource_DoesNotReferenceEntityFramework()
    {
        var applicationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ECommerceBackend.Application");
        var forbiddenReferences = Directory
            .EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = index + 1,
                    Text = line
                }))
            .Where(source => source.Text.Contains(
                "Microsoft.EntityFrameworkCore",
                StringComparison.Ordinal)
                || source.Text.Contains("DbUpdateException", StringComparison.Ordinal)
                || source.Text.Contains("DbUpdateConcurrencyException", StringComparison.Ordinal))
            .Select(source => $"{source.File}:{source.Line}")
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void ApplicationLayer_DoesNotReferenceWebOrSecurityImplementations()
    {
        var applicationDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ECommerceBackend.Application");
        var forbiddenTokens = new[]
        {
            "Microsoft.AspNetCore",
            "IFormFile",
            "IHttpContextAccessor",
            "IWebHostEnvironment",
            "System.IdentityModel",
            "Microsoft.IdentityModel",
            "BCrypt.Net"
        };
        var sourceFiles = Directory
            .EnumerateFiles(applicationDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                && !file.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Append(Path.Combine(
                applicationDirectory,
                "ECommerceBackend.Application.csproj"));
        var forbiddenReferences = sourceFiles
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = index + 1,
                    Text = line
                }))
            .Where(source => forbiddenTokens.Any(token => source.Text.Contains(
                token,
                StringComparison.Ordinal)))
            .Select(source => $"{source.File}:{source.Line}")
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void UnitTestProject_DoesNotReferenceApiOrPersistence()
    {
        var unitTestDirectory = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ECommerceBackend.UnitTests");
        var forbiddenReferences = Directory
            .EnumerateFiles(unitTestDirectory, "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase)
                && !file.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal)
                || file.EndsWith(".csproj", StringComparison.Ordinal))
            .SelectMany(file => File.ReadLines(file)
                .Select((line, index) => new
                {
                    File = file,
                    Line = index + 1,
                    Text = line
                }))
            .Where(source => source.Text.Contains(
                    "ECommerceBackend.API",
                    StringComparison.Ordinal)
                || source.Text.Contains(
                    "ECommerceBackend.Infrastructure",
                    StringComparison.Ordinal)
                || source.Text.Contains(
                    "Microsoft.EntityFrameworkCore",
                    StringComparison.Ordinal)
                || source.Text.Contains(
                    "TestAppDbContext",
                    StringComparison.Ordinal))
            .Select(source => $"{source.File}:{source.Line}")
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void RepositoryContracts_DoNotExposeQueryableOrDbSet()
    {
        var repositoryContracts = typeof(IOrderRepository).Assembly
            .GetTypes()
            .Where(type => type.IsInterface
                && type.Namespace == typeof(IOrderRepository).Namespace);
        var exposedTypes = repositoryContracts
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method
                .GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType));

        Assert.DoesNotContain(exposedTypes, ContainsPersistenceQueryType);
    }

    [Fact]
    public void Checkout_IsOwnedByOrderCheckoutUseCase()
    {
        var lifecycleUseCases = new[]
        {
            typeof(OrderStatusUpdateUseCase),
            typeof(CustomerOrderCancellationUseCase),
            typeof(PendingOrderExpirationUseCase),
            typeof(ShipmentDispatchUseCase),
            typeof(ShipmentDeliveryUseCase),
            typeof(OrderReturnRequestUseCase),
            typeof(OrderReturnReviewUseCase),
            typeof(OrderReturnReceiptUseCase)
        };
        var commandMethods = lifecycleUseCases.SelectMany(type =>
            type.GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly));

        Assert.DoesNotContain(
            commandMethods,
            method => method.Name == "PlaceOrderAsync");
        Assert.NotNull(typeof(OrderCheckoutUseCase).GetMethod(
            nameof(OrderCheckoutUseCase.ExecuteAsync)));
    }

    [Fact]
    public void CriticalControllers_KeepDependingOnStableServiceInterfaces()
    {
        AssertConstructorDependency<AuthController, IAuthService>();
        AssertConstructorDependency<OrderController, IOrderService>();
        AssertConstructorDependency<OperationsController, IOperationsService>();
    }

    private static void AssertPublicMethodCount<T>(int expected)
    {
        var methods = typeof(T)
            .GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();
        Assert.Equal(expected, methods.Length);
    }

    private static void AssertConstructorDependency<TConsumer, TDependency>()
    {
        var dependencies = typeof(TConsumer)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType);
        Assert.Contains(typeof(TDependency), dependencies);
    }

    private static bool ContainsPersistenceQueryType(Type type)
    {
        if (type.IsGenericType)
        {
            var genericType = type.GetGenericTypeDefinition();
            if (genericType == typeof(IQueryable<>)
                || genericType.FullName == "Microsoft.EntityFrameworkCore.DbSet`1")
            {
                return true;
            }

            return type.GetGenericArguments().Any(ContainsPersistenceQueryType);
        }

        return false;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ECommerceBackend.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private static string[] ReferencedAssemblyNames(System.Reflection.Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();
}
