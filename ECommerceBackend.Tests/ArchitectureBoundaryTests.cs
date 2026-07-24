using ECommerceBackend.API.Controllers;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;

namespace ECommerceBackend.Tests;

public sealed class ArchitectureBoundaryTests
{
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
            typeof(IAppDbContext),
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
        AssertPublicMethodCount<AuthSessionService>(4);
        AssertPublicMethodCount<PasswordResetUseCase>(2);
        AssertPublicMethodCount<OrderQueryUseCase>(3);
        AssertPublicMethodCount<OrderCheckoutUseCase>(1);
        AssertPublicMethodCount<OrderCommandService>(4);
        AssertPublicMethodCount<DeadLetterUseCase>(2);
        AssertPublicMethodCount<AuditQueryUseCase>(1);
        AssertPublicMethodCount<DataRetentionUseCase>(1);
    }

    [Fact]
    public void RemovedGenericRepository_IsNotPartOfApplicationOrInfrastructure()
    {
        var productionTypes = typeof(AuthService).Assembly.GetTypes();

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
}
