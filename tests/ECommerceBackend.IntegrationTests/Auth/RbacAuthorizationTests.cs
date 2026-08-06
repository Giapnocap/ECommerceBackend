using ECommerceBackend.API.Controllers;
using ECommerceBackend.API.Extensions;
using ECommerceBackend.API.Swagger;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerceBackend.Tests;

public sealed class RbacAuthorizationTests
{
    [Fact]
    public async Task RolePermissionSeed_MatchesExpectedMatrix()
    {
        await using var context = TestAppDbContext.Create();
        var assignments = await context.RolePermissions
            .AsNoTracking()
            .Include(rolePermission => rolePermission.Role)
            .Include(rolePermission => rolePermission.Permission)
            .ToListAsync();

        string[] PermissionsFor(string roleName) => assignments
            .Where(item => item.Role?.Name == roleName && item.Permission != null)
            .Select(item => item.Permission!.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            PermissionNames.All.OrderBy(name => name, StringComparer.Ordinal),
            PermissionsFor(RoleNames.Admin));
        Assert.Equal(
            PermissionNames.StaffPermissions.OrderBy(name => name, StringComparer.Ordinal),
            PermissionsFor(RoleNames.Staff));
        Assert.Empty(PermissionsFor(RoleNames.Customer));
    }

    [Fact]
    public void UserRoleModel_EnforcesOneRolePerUser()
    {
        using var context = TestAppDbContext.Create();
        var entityType = context.Model.FindEntityType(typeof(UserRole));

        var uniqueUserIndex = entityType?.GetIndexes().SingleOrDefault(index =>
            index.IsUnique
            && index.Properties.Count == 1
            && index.Properties[0].Name == nameof(UserRole.UserId));

        Assert.NotNull(uniqueUserIndex);
    }

    [Fact]
    public async Task AuthorizationPolicies_RequireAuthenticationRolesAndPermissions()
    {
        var services = new ServiceCollection();
        services.AddECommerceAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallbackPolicy = await policyProvider.GetFallbackPolicyAsync();
        Assert.NotNull(fallbackPolicy);
        Assert.Contains(
            fallbackPolicy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);

        var customerPolicy = await policyProvider.GetPolicyAsync(AuthorizationPolicyNames.CustomerAccess);
        var customerRoleRequirement = Assert.Single(
            Assert.IsType<AuthorizationPolicy>(customerPolicy)
                .Requirements
                .OfType<RolesAuthorizationRequirement>());
        Assert.Equal(RoleNames.Customer, Assert.Single(customerRoleRequirement.AllowedRoles));

        foreach (var permission in PermissionNames.All)
        {
            var policy = await policyProvider.GetPolicyAsync(permission);
            var claimRequirement = Assert.Single(
                Assert.IsType<AuthorizationPolicy>(policy)
                    .Requirements
                    .OfType<ClaimsAuthorizationRequirement>());
            Assert.Equal(AuthClaimTypes.Permission, claimRequirement.ClaimType);
            Assert.Equal(permission, Assert.Single(claimRequirement.AllowedValues!));
        }
    }

    [Fact]
    public void PublicEndpoints_AreExplicitlyAnonymousAndReviewed()
    {
        var approvedAnonymousActions = new HashSet<string>(StringComparer.Ordinal)
        {
            $"{nameof(AuthController)}.{nameof(AuthController.Register)}",
            $"{nameof(AuthController)}.{nameof(AuthController.Login)}",
            $"{nameof(AuthController)}.{nameof(AuthController.Refresh)}",
            $"{nameof(AuthController)}.{nameof(AuthController.ForgotPassword)}",
            $"{nameof(AuthController)}.{nameof(AuthController.ResetPassword)}",
            $"{nameof(ProductController)}.{nameof(ProductController.GetAll)}",
            $"{nameof(ProductController)}.{nameof(ProductController.GetSummaries)}",
            $"{nameof(ProductController)}.{nameof(ProductController.GetById)}",
            $"{nameof(CategoryController)}.{nameof(CategoryController.GetAll)}",
            $"{nameof(CategoryController)}.{nameof(CategoryController.GetById)}",
            $"{nameof(PaymentController)}.{nameof(PaymentController.GetMethods)}",
            $"{nameof(PaymentController)}.{nameof(PaymentController.HandleWebhook)}"
        };

        var actualAnonymousActions = GetControllerActions()
            .Where(action => action.DeclaringType!.IsDefined(
                    typeof(AllowAnonymousAttribute),
                    inherit: true)
                || action.IsDefined(typeof(AllowAnonymousAttribute), inherit: true))
            .Select(action => $"{action.DeclaringType!.Name}.{action.Name}")
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            approvedAnonymousActions.OrderBy(name => name, StringComparer.Ordinal),
            actualAnonymousActions.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void SwaggerSecurityMetadata_MatchesAuthenticatedFallbackPolicy()
    {
        foreach (var action in GetControllerActions())
        {
            var allowsAnonymous = action.DeclaringType!.IsDefined(
                    typeof(AllowAnonymousAttribute),
                    inherit: true)
                || action.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);

            Assert.Equal(
                !allowsAnonymous,
                SwaggerAuthorizationMetadata.RequiresAuthorization(action));
        }
    }
    [Fact]
    public void CustomerCommerceEndpoints_RequireCustomerPolicy()
    {
        AssertControllerPolicy<CartController>(AuthorizationPolicyNames.CustomerAccess);
        AssertActionPolicy<OrderController>(nameof(OrderController.PlaceOrder), AuthorizationPolicyNames.CustomerAccess);
        AssertActionPolicy<OrderController>(nameof(OrderController.GetMyOrders), AuthorizationPolicyNames.CustomerAccess);
        AssertActionPolicy<OrderController>(nameof(OrderController.Cancel), AuthorizationPolicyNames.CustomerAccess);
    }

    [Fact]
    public void OperationalEndpoints_RequireExpectedPermissions()
    {
        AssertActionPolicy<UserController>(nameof(UserController.GetAllUsers), PermissionNames.ManageUsers);
        AssertActionPolicy<UserController>(nameof(UserController.AssignRole), PermissionNames.ManageUsers);
        AssertActionPolicy<ProductController>(nameof(ProductController.Create), PermissionNames.ManageProducts);
        AssertActionPolicy<ProductController>(nameof(ProductController.Update), PermissionNames.ManageProducts);
        AssertActionPolicy<ProductController>(nameof(ProductController.Delete), PermissionNames.ManageProducts);
        AssertActionPolicy<CategoryController>(nameof(CategoryController.Create), PermissionNames.ManageCategories);
        AssertActionPolicy<CategoryController>(nameof(CategoryController.Update), PermissionNames.ManageCategories);
        AssertActionPolicy<CategoryController>(nameof(CategoryController.Delete), PermissionNames.ManageCategories);
        AssertActionPolicy<OrderController>(nameof(OrderController.GetAllOrders), PermissionNames.ProcessOrders);
        AssertActionPolicy<OrderController>(nameof(OrderController.UpdateStatus), PermissionNames.ProcessOrders);
        AssertControllerPolicy<InventoryController>(PermissionNames.ViewInventory);
        AssertControllerPolicy<ReportController>(PermissionNames.ViewReports);
    }

    [Fact]
    public void OperationsEndpoints_RequireAdminRole()
    {
        var attributes = typeof(OperationsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();

        Assert.Contains(attributes, attribute => attribute.Roles == RoleNames.Admin);
    }

    private static IReadOnlyList<System.Reflection.MethodInfo> GetControllerActions()
        => typeof(AuthController).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(method => method
                .GetCustomAttributes(inherit: true)
                .OfType<HttpMethodAttribute>()
                .Any())
            .ToArray();
    private static void AssertControllerPolicy<TController>(string policy)
    {
        var attributes = typeof(TController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, attribute => attribute.Policy == policy);
    }

    private static void AssertActionPolicy<TController>(string actionName, string policy)
    {
        var method = typeof(TController).GetMethod(actionName);
        Assert.NotNull(method);
        var attributes = method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>();
        Assert.Contains(attributes, attribute => attribute.Policy == policy);
    }
}
