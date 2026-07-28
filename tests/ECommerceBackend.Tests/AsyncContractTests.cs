using System.Reflection;
using ECommerceBackend.API.Controllers;
using ECommerceBackend.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.Tests;

public sealed class AsyncContractTests
{
    [Fact]
    public void ApplicationAsyncMethods_AcceptCancellationToken()
    {
        var violations = typeof(IAuthService).Assembly
            .GetTypes()
            .Where(type => type.IsInterface
                && type.Namespace == typeof(IAuthService).Namespace)
            .SelectMany(type => type.GetMethods()
                .Where(IsTaskReturning)
                .Where(method => !AcceptsCancellationToken(method))
                .Select(method => $"{type.Name}.{method.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Async application contracts missing CancellationToken: {string.Join(", ", violations)}");
    }

    [Fact]
    public void AsyncControllerActions_AcceptCancellationToken()
    {
        var violations = typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract
                && typeof(ControllerBase).IsAssignableFrom(type)
                && type.Namespace == typeof(AuthController).Namespace)
            .SelectMany(type => type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(IsTaskReturning)
                .Where(method => !AcceptsCancellationToken(method))
                .Select(method => $"{type.Name}.{method.Name}"))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Async controller actions missing CancellationToken: {string.Join(", ", violations)}");
    }

    private static bool IsTaskReturning(MethodInfo method)
        => typeof(Task).IsAssignableFrom(method.ReturnType);

    private static bool AcceptsCancellationToken(MethodInfo method)
        => method.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(CancellationToken));
}
