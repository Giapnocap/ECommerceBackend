using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task RegisterAsync_CreatesCustomerUserCartAndRefreshToken()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);

        var response = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "phase7_customer",
            Email = "phase7_customer@example.com",
            Password = "Customer@123",
            FullName = "Phase 7 Customer",
            Phone = "0901234567"
        });

        var user = await context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .SingleAsync(u => u.Id == response.UserId);

        Assert.Equal("phase7_customer", response.UserName);
        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Contains("Customer", response.Roles);
        Assert.Contains(user.UserRoles, role => role.Role?.Name == "Customer");
        Assert.True(await context.Carts.AnyAsync(cart => cart.UserId == response.UserId));

        var refreshTokens = await context.RefreshTokens
            .Where(token => token.UserId == response.UserId)
            .ToListAsync();
        Assert.Contains(refreshTokens, token => !token.IsRevoked);
    }

    [Fact]
    public async Task SessionCommands_UseInjectedUtcClock()
    {
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now));

        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "clock_customer",
            Email = "clock_customer@example.com",
            Password = "Customer@123",
            FullName = "Clock Customer"
        });
        await service.LogoutAllAsync(registered.UserId);

        var token = await context.RefreshTokens.SingleAsync(candidate =>
            candidate.UserId == registered.UserId);
        Assert.Equal(now.UtcDateTime, token.CreatedAt);
        Assert.Equal(now.AddDays(7).UtcDateTime, token.ExpiresAt);
        Assert.Equal(now.UtcDateTime, token.RevokedAt);
        Assert.Equal(now.AddMinutes(60).UtcDateTime, registered.AccessTokenExpiresAt);
    }

    [Fact]
    public async Task RegisterAsync_WithDuplicateUserName_ThrowsConflict()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);
        var request = new RegisterRequest
        {
            UserName = "duplicate_customer",
            Email = "duplicate_customer@example.com",
            Password = "Customer@123",
            FullName = "Duplicate Customer"
        };

        await service.RegisterAsync(request);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new RegisterRequest
            {
                UserName = request.UserName,
                Email = "duplicate_customer_2@example.com",
                Password = request.Password,
                FullName = request.FullName
            }));

        Assert.Equal(409, exception.StatusCode);
        Assert.Equal("username_conflict", exception.Code);
    }

    [Fact]
    public async Task RefreshAsync_RotatesToken_AndLogoutRevokesCurrentToken()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);

        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "rotation_customer",
            Email = "rotation_customer@example.com",
            Password = "Customer@123",
            FullName = "Rotation Customer"
        });

        var refreshed = await service.RefreshAsync(new RefreshTokenRequest
        {
            RefreshToken = registered.RefreshToken
        });

        Assert.NotEqual(registered.RefreshToken, refreshed.RefreshToken);

        var tokensAfterRefresh = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();

        Assert.Equal(2, tokensAfterRefresh.Count);
        Assert.Single(tokensAfterRefresh, token => token.IsRevoked && token.ReplacedByTokenHash != null);
        Assert.Single(tokensAfterRefresh, token => token.IsActiveAt(DateTime.UtcNow));

        await service.LogoutAsync(refreshed.UserId, new LogoutRequest
        {
            RefreshToken = refreshed.RefreshToken
        });

        var tokensAfterLogout = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();
        Assert.DoesNotContain(tokensAfterLogout, token => token.IsActiveAt(DateTime.UtcNow));
    }

    [Fact]
    public async Task RefreshAsync_WhenRotatedTokenIsReused_RevokesEntireTokenFamily()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "reuse_customer",
            Email = "reuse_customer@example.com",
            Password = "Customer@123",
            FullName = "Reuse Customer"
        });
        var refreshed = await service.RefreshAsync(new RefreshTokenRequest
        {
            RefreshToken = registered.RefreshToken
        });

        await Assert.ThrowsAsync<ApiException>(() => service.RefreshAsync(new RefreshTokenRequest
        {
            RefreshToken = registered.RefreshToken
        }));
        await Assert.ThrowsAsync<ApiException>(() => service.RefreshAsync(new RefreshTokenRequest
        {
            RefreshToken = refreshed.RefreshToken
        }));

        var tokens = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();
        Assert.DoesNotContain(tokens, token => token.IsActiveAt(DateTime.UtcNow));
        Assert.Contains(tokens, token => token.RevocationReason == "Refresh token reuse detected");
    }

    [Fact]
    public async Task LogoutAllAsync_RevokesEverySessionAndIncrementsTokenVersion()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "logout_all_customer",
            Email = "logout_all_customer@example.com",
            Password = "Customer@123",
            FullName = "Logout All Customer"
        });
        _ = await service.LoginAsync(new LoginRequest
        {
            UserName = "logout_all_customer",
            Password = "Customer@123"
        });

        await service.LogoutAllAsync(registered.UserId);

        var user = await context.Users.SingleAsync(candidate => candidate.Id == registered.UserId);
        var tokens = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();
        Assert.Equal(1, user.TokenVersion);
        Assert.DoesNotContain(tokens, token => token.IsActiveAt(DateTime.UtcNow));
    }
}
