using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public class UserServiceTests
{
    [Fact]
    public async Task ChangePasswordAsync_RevokesSessionsAndInvalidatesExistingAccessTokens()
    {
        await using var context = TestAppDbContext.Create();
        var authService = TestServiceFactory.CreateAuthService(context);
        var registered = await authService.RegisterAsync(new RegisterRequest
        {
            UserName = "password_customer",
            Email = "password_customer@example.com",
            Password = "Customer@123",
            FullName = "Password Customer"
        });
        var userService = TestServiceFactory.CreateUserService(context);

        await userService.ChangePasswordAsync(registered.UserId, new ChangePasswordRequest
        {
            CurrentPassword = "Customer@123",
            NewPassword = "NewCustomer@456"
        });

        var user = await context.Users.SingleAsync(candidate => candidate.Id == registered.UserId);
        Assert.True(BCrypt.Net.BCrypt.Verify("NewCustomer@456", user.PasswordHash));
        Assert.Equal(1, user.TokenVersion);
        Assert.DoesNotContain(
            await context.RefreshTokens.Where(token => token.UserId == user.Id).ToListAsync(),
            token => token.IsActiveAt(DateTime.UtcNow));
    }

    [Fact]
    public async Task AssignRoleAsync_CannotDemoteLastActiveAdmin()
    {
        await using var context = TestAppDbContext.Create();
        var adminRole = await context.Roles.SingleAsync(role => role.Name == "Admin");
        var admin = User("last_admin", "last_admin@example.com");
        context.Users.Add(admin);
        context.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateUserService(context);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.AssignRoleAsync(Guid.NewGuid(), admin.Id, new AssignRoleRequest
            {
                RoleName = "Customer"
            }));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("last_admin_demotion_forbidden", exception.Code);
        Assert.True(await context.UserRoles.AnyAsync(userRole => userRole.UserId == admin.Id
            && userRole.RoleId == adminRole.Id));
    }

    [Fact]
    public async Task AssignRoleAsync_ReplacesRoleAndRevokesExistingSessions()
    {
        await using var context = TestAppDbContext.Create();
        var authService = TestServiceFactory.CreateAuthService(context);
        var registered = await authService.RegisterAsync(new RegisterRequest
        {
            UserName = "role_customer",
            Email = "role_customer@example.com",
            Password = "Customer@123",
            FullName = "Role Customer"
        });
        var adminRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Admin);
        var actor = User("role_admin", "role_admin@example.com");
        context.Users.Add(actor);
        context.UserRoles.Add(new UserRole { UserId = actor.Id, RoleId = adminRole.Id });
        await context.SaveChangesAsync();
        Assert.Contains(
            await context.RefreshTokens.Where(token => token.UserId == registered.UserId).ToListAsync(),
            token => token.IsActiveAt(DateTime.UtcNow));
        var service = TestServiceFactory.CreateUserService(context);

        await service.AssignRoleAsync(actor.Id, registered.UserId, new AssignRoleRequest
        {
            RoleName = RoleNames.Staff
        });

        context.ChangeTracker.Clear();
        var user = await context.Users
            .Include(candidate => candidate.UserRoles)
                .ThenInclude(userRole => userRole.Role)
            .SingleAsync(candidate => candidate.Id == registered.UserId);
        var tokens = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();

        Assert.Equal(1, user.TokenVersion);
        Assert.Equal(RoleNames.Staff, Assert.Single(user.UserRoles).Role?.Name);
        Assert.All(tokens, token =>
        {
            Assert.False(token.IsActiveAt(DateTime.UtcNow));
            Assert.Equal("Role changed", token.RevocationReason);
        });
    }

    [Fact]
    public async Task AssignRoleAsync_CannotChangeOwnRole()
    {
        await using var context = TestAppDbContext.Create();
        var adminRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Admin);
        var firstAdmin = User("first_admin", "first_admin@example.com");
        var secondAdmin = User("second_admin", "second_admin@example.com");
        context.Users.AddRange(firstAdmin, secondAdmin);
        context.UserRoles.AddRange(
            new UserRole { UserId = firstAdmin.Id, RoleId = adminRole.Id },
            new UserRole { UserId = secondAdmin.Id, RoleId = adminRole.Id });
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateUserService(context);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.AssignRoleAsync(firstAdmin.Id, firstAdmin.Id, new AssignRoleRequest
            {
                RoleName = RoleNames.Staff
            }));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("role_self_change_forbidden", exception.Code);
        Assert.Contains("chính mình", exception.Message);
        Assert.True(await context.UserRoles.AnyAsync(userRole =>
            userRole.UserId == firstAdmin.Id && userRole.RoleId == adminRole.Id));
    }

    [Fact]
    public async Task GetAllUsersAsync_FiltersByKeywordAndRoleAndPaginates()
    {
        await using var context = TestAppDbContext.Create();
        var customerRole = await context.Roles.SingleAsync(role => role.Name == "Customer");
        var users = new[]
        {
            User("alice_one", "alice.one@example.com"),
            User("alice_two", "alice.two@example.com"),
            User("bob", "bob@example.com")
        };
        context.Users.AddRange(users);
        context.UserRoles.AddRange(users.Select(user => new UserRole
        {
            UserId = user.Id,
            RoleId = customerRole.Id
        }));
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateUserService(context);

        var result = await service.GetAllUsersAsync(new UserQueryParams
        {
            Keyword = "alice",
            Role = "Customer",
            Page = 2,
            PageSize = 1
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Page);
        Assert.Single(result.Items);
        Assert.Contains("alice", result.Items.Single().UserName);
    }

    private static User User(string userName, string email)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = DateTime.UtcNow
        };
}
