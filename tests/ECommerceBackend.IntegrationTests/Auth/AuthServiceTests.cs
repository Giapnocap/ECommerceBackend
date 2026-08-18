using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Security;
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
            UserName = "registration_customer",
            Email = "registration_customer@example.com",
            Password = "Customer@123",
            FullName = "Registration Customer",
            Phone = "0901234567"
        });

        var user = await context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .SingleAsync(u => u.Id == response.UserId);

        Assert.Equal("registration_customer", response.UserName);
        Assert.NotEmpty(response.AccessToken);
        Assert.NotEmpty(response.RefreshToken);
        Assert.Contains("Customer", response.Roles);
        Assert.Contains(user.UserRoles, role => role.Role?.Name == "Customer");
        Assert.True(await context.Carts.AnyAsync(cart => cart.UserId == response.UserId));

        var refreshTokens = await context.RefreshTokens
            .Where(token => token.UserId == response.UserId)
            .ToListAsync();
        Assert.Contains(refreshTokens, token => !token.IsRevoked);
        Assert.False(response.EmailVerified);
        Assert.Single(await context.EmailVerificationTokens
            .Where(token => token.UserId == response.UserId)
            .ToListAsync());
    }

    [Fact]
    public async Task SessionManagement_ListsAndRevokesOnlySelectedSession()
    {
        var now = new DateTimeOffset(2026, 8, 18, 13, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now));
        var first = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "session_customer",
            Email = "session_customer@example.com",
            Password = "Customer@123",
            FullName = "Session Customer"
        });
        var second = await service.LoginAsync(new LoginRequest
        {
            UserName = "session_customer",
            Password = "Customer@123"
        });

        var sessions = await service.GetSessionsAsync(
            first.UserId,
            first.SessionId);
        Assert.Equal(2, sessions.Count);
        Assert.Single(sessions, session => session.IsCurrent);
        Assert.Contains(sessions, session => session.SessionId == second.SessionId);

        await service.RevokeSessionAsync(first.UserId, second.SessionId);

        var remaining = await service.GetSessionsAsync(
            first.UserId,
            first.SessionId);
        var current = Assert.Single(remaining);
        Assert.Equal(first.SessionId, current.SessionId);
        await Assert.ThrowsAsync<ApiException>(() => service.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = second.RefreshToken }));
        var refreshedFirst = await service.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = first.RefreshToken });
        Assert.Equal(first.SessionId, refreshedFirst.SessionId);
    }

    [Fact]
    public async Task EmailVerification_IsHashedExpiringAndSingleUse()
    {
        var now = new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero);
        var protector = new TestSensitivePayloadProtector();
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now),
            payloadProtector: protector);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "verified_customer",
            Email = "verified_customer@example.com",
            Password = "Customer@123",
            FullName = "Verified Customer"
        });
        var (message, payload) = await GetProtectedNotificationAsync(
            context,
            protector,
            "Xác minh địa chỉ email");
        var verificationUrl = new Uri(
            payload.Message.Split('\n')[0].Split(": ", 2)[1]);
        var rawToken = Uri.UnescapeDataString(
            verificationUrl.Query["?token=".Length..]);
        var storedToken = await context.EmailVerificationTokens.SingleAsync();

        Assert.Equal(64, storedToken.TokenHash.Length);
        Assert.NotEqual(rawToken, storedToken.TokenHash);
        Assert.DoesNotContain(rawToken, message.Payload, StringComparison.Ordinal);

        await service.ConfirmEmailAsync(
            new ConfirmEmailRequest { Token = rawToken });

        var user = await context.Users.SingleAsync(candidate =>
            candidate.Id == registered.UserId);
        Assert.Equal(now.UtcDateTime, user.EmailVerifiedAt);
        Assert.Equal(now.UtcDateTime, storedToken.ConsumedAt);
        var reused = await Assert.ThrowsAsync<ApiException>(() =>
            service.ConfirmEmailAsync(
                new ConfirmEmailRequest { Token = rawToken }));
        Assert.Equal("invalid_email_verification_token", reused.Code);
    }

    [Fact]
    public async Task EmailVerification_ExpiredTokenDoesNotVerifyUser()
    {
        var now = new DateTimeOffset(2026, 8, 18, 15, 0, 0, TimeSpan.Zero);
        var protector = new TestSensitivePayloadProtector();
        var options = new AuthSecurityOptions
        {
            EmailVerificationTokenMinutes = 5
        };
        await using var context = TestAppDbContext.Create();
        var issuingService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now),
            options,
            payloadProtector: protector);
        var registered = await issuingService.RegisterAsync(new RegisterRequest
        {
            UserName = "expired_verification",
            Email = "expired_verification@example.com",
            Password = "Customer@123",
            FullName = "Expired Verification"
        });
        var (_, payload) = await GetProtectedNotificationAsync(
            context,
            protector,
            "Xác minh địa chỉ email");
        var verificationUrl = new Uri(
            payload.Message.Split('\n')[0].Split(": ", 2)[1]);
        var rawToken = Uri.UnescapeDataString(
            verificationUrl.Query["?token=".Length..]);
        var expiredService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now.AddMinutes(6)),
            options,
            payloadProtector: protector);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            expiredService.ConfirmEmailAsync(
                new ConfirmEmailRequest { Token = rawToken }));

        Assert.Equal("invalid_email_verification_token", exception.Code);
        var user = await context.Users.SingleAsync(candidate =>
            candidate.Id == registered.UserId);
        Assert.Null(user.EmailVerifiedAt);
        Assert.Null((await context.EmailVerificationTokens.SingleAsync()).ConsumedAt);
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
    public async Task RefreshAsync_WithExpiredToken_IsRejectedWithoutMutatingSession()
    {
        var issuedAt = new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var issuingService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(issuedAt));
        var registered = await issuingService.RegisterAsync(new RegisterRequest
        {
            UserName = "expired_refresh_customer",
            Email = "expired_refresh_customer@example.com",
            Password = "Customer@123",
            FullName = "Expired Refresh Customer"
        });
        var expiredService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(issuedAt.AddDays(8)));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            expiredService.RefreshAsync(new RefreshTokenRequest
            {
                RefreshToken = registered.RefreshToken
            }));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("unauthorized", exception.Code);
        var token = await context.RefreshTokens.SingleAsync(candidate =>
            candidate.UserId == registered.UserId);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.ReplacedByTokenHash);
    }

    [Fact]
    public async Task RefreshTokenReuse_RevokesOnlyCompromisedDeviceFamily()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);
        var firstDevice = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "multi_device_customer",
            Email = "multi_device_customer@example.com",
            Password = "Customer@123",
            FullName = "Multi Device Customer"
        });
        var secondDevice = await service.LoginAsync(new LoginRequest
        {
            UserName = "multi_device_customer",
            Password = "Customer@123"
        });
        _ = await service.RefreshAsync(new RefreshTokenRequest
        {
            RefreshToken = firstDevice.RefreshToken
        });

        await Assert.ThrowsAsync<ApiException>(() =>
            service.RefreshAsync(new RefreshTokenRequest
            {
                RefreshToken = firstDevice.RefreshToken
            }));
        var secondDeviceRefresh = await service.RefreshAsync(
            new RefreshTokenRequest
            {
                RefreshToken = secondDevice.RefreshToken
            });

        Assert.NotEqual(secondDevice.RefreshToken, secondDeviceRefresh.RefreshToken);
        var families = (await context.RefreshTokens
                .Where(token => token.UserId == firstDevice.UserId)
                .ToListAsync())
            .GroupBy(token => token.FamilyId)
            .ToList();
        Assert.Equal(2, families.Count);
        Assert.Single(
            families,
            family => family.All(token => token.IsRevoked));
        Assert.Single(
            families,
            family => family.Any(token => token.IsActiveAt(DateTime.UtcNow)));
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

    [Fact]
    public async Task LoginAsync_ForUnknownUser_StillRunsPasswordVerification()
    {
        await using var context = TestAppDbContext.Create();
        var passwordHasher = new RecordingPasswordHasher();
        var service = TestServiceFactory.CreateAuthService(
            context,
            passwordHasher: passwordHasher);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            service.LoginAsync(new LoginRequest
            {
                UserName = "unknown_customer",
                Password = "Wrong@123"
            }));

        Assert.Equal(401, exception.StatusCode);
        Assert.Equal("unauthorized", exception.Code);
        Assert.Single(passwordHasher.Verifications);
        Assert.Null(passwordHasher.Verifications[0].PasswordHash);
    }

    [Fact]
    public async Task RefreshAsync_RejectsLockedAccountWithoutRotatingItsSession()
    {
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now));
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "refresh_locked_customer",
            Email = "refresh_locked_customer@example.com",
            Password = "Customer@123",
            FullName = "Refresh Locked Customer"
        });
        var user = await context.Users.SingleAsync(candidate => candidate.Id == registered.UserId);
        user.RecordFailedLogin(now.UtcDateTime, maxFailedAttempts: 1, TimeSpan.FromMinutes(15));
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.RefreshAsync(
            new RefreshTokenRequest { RefreshToken = registered.RefreshToken }));

        Assert.Equal(401, exception.StatusCode);
        var tokens = await context.RefreshTokens
            .Where(token => token.UserId == registered.UserId)
            .ToListAsync();
        Assert.Single(tokens);
        Assert.True(tokens[0].IsActiveAt(now.UtcDateTime));
    }

    [Fact]
    public async Task LoginAsync_LocksAfterConfiguredFailures_ThenAutomaticallyUnlocks()
    {
        var now = new DateTimeOffset(2026, 7, 24, 10, 0, 0, TimeSpan.Zero);
        var securityOptions = new AuthSecurityOptions
        {
            MaxFailedLoginAttempts = 2,
            LockoutMinutes = 15
        };
        var audit = new RecordingAuditWriter();
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now),
            securityOptions,
            auditWriter: audit);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "locked_customer",
            Email = "locked_customer@example.com",
            Password = "Customer@123",
            FullName = "Locked Customer"
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var exception = await Assert.ThrowsAsync<ApiException>(() =>
                service.LoginAsync(new LoginRequest
                {
                    UserName = "locked_customer",
                    Password = "Wrong@123"
                }));
            Assert.Equal(401, exception.StatusCode);
        }

        var lockedUser = await context.Users.SingleAsync(user => user.Id == registered.UserId);
        Assert.Equal(2, lockedUser.FailedLoginCount);
        Assert.Equal(now.AddMinutes(15).UtcDateTime, lockedUser.LockoutEndAt);
        Assert.Single(audit.Actions, action => action == "auth.account.locked");

        await Assert.ThrowsAsync<ApiException>(() =>
            service.LoginAsync(new LoginRequest
            {
                UserName = "locked_customer",
                Password = "Customer@123"
            }));

        var unlockedService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now.AddMinutes(16)),
            securityOptions,
            auditWriter: audit);
        var response = await unlockedService.LoginAsync(new LoginRequest
        {
            UserName = "locked_customer",
            Password = "Customer@123"
        });

        Assert.Equal(registered.UserId, response.UserId);
        Assert.Equal(0, lockedUser.FailedLoginCount);
        Assert.Null(lockedUser.LockoutEndAt);
    }

    [Fact]
    public async Task PasswordReset_IsSingleUse_RevokesSessions_AndDoesNotPersistRawToken()
    {
        var now = new DateTimeOffset(2026, 7, 24, 11, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var protector = new TestSensitivePayloadProtector();
        var audit = new RecordingAuditWriter();
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            clock,
            auditWriter: audit,
            payloadProtector: protector);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "reset_customer",
            Email = "reset_customer@example.com",
            Password = "Customer@123",
            FullName = "Reset Customer"
        });

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "reset_customer@example.com"
        });

        var resetToken = await context.PasswordResetTokens.SingleAsync();
        var (outboxMessage, payload) = await GetProtectedNotificationAsync(
            context,
            protector,
            "Đặt lại mật khẩu");
        var resetUrl = new Uri(payload.Message.Split('\n')[0].Split(": ", 2)[1]);
        var rawToken = Uri.UnescapeDataString(resetUrl.Query["?token=".Length..]);

        Assert.Equal(64, resetToken.TokenHash.Length);
        Assert.NotEqual(rawToken, resetToken.TokenHash);
        Assert.DoesNotContain(rawToken, outboxMessage.Payload, StringComparison.Ordinal);

        await service.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = rawToken,
            NewPassword = "Changed@123"
        });

        Assert.Equal(now.UtcDateTime, resetToken.ConsumedAt);
        var user = await context.Users.SingleAsync(candidate =>
            candidate.Id == registered.UserId);
        Assert.Equal(1, user.TokenVersion);
        Assert.All(
            await context.RefreshTokens
                .Where(token => token.UserId == registered.UserId)
                .ToListAsync(),
            token => Assert.True(token.IsRevoked));
        Assert.Contains("auth.password_reset.requested", audit.Actions);
        Assert.Contains("auth.password_reset.completed", audit.Actions);

        var reusedException = await Assert.ThrowsAsync<ApiException>(() =>
            service.ResetPasswordAsync(new ResetPasswordRequest
            {
                Token = rawToken,
                NewPassword = "Another@123"
            }));
        Assert.Equal("invalid_password_reset_token", reusedException.Code);

        await Assert.ThrowsAsync<ApiException>(() =>
            service.LoginAsync(new LoginRequest
            {
                UserName = "reset_customer",
                Password = "Customer@123"
            }));
        var login = await service.LoginAsync(new LoginRequest
        {
            UserName = "reset_customer",
            Password = "Changed@123"
        });
        Assert.Equal(registered.UserId, login.UserId);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_ForUnknownEmail_HasNoObservableSideEffect()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);

        await service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "unknown@example.com"
        });

        Assert.Empty(await context.PasswordResetTokens.ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task ResetPasswordAsync_WithExpiredToken_DoesNotMutateUserOrSessions()
    {
        var now = new DateTimeOffset(2026, 7, 24, 14, 0, 0, TimeSpan.Zero);
        var protector = new TestSensitivePayloadProtector();
        var securityOptions = new AuthSecurityOptions
        {
            PasswordResetTokenMinutes = 5
        };
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now),
            securityOptions,
            payloadProtector: protector);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            UserName = "expired_reset_customer",
            Email = "expired_reset_customer@example.com",
            Password = "Customer@123",
            FullName = "Expired Reset Customer"
        });
        await service.RequestPasswordResetAsync(new ForgotPasswordRequest
        {
            Email = "expired_reset_customer@example.com"
        });
        var (_, payload) = await GetProtectedNotificationAsync(
            context,
            protector,
            "Đặt lại mật khẩu");
        var resetUrl = new Uri(payload.Message.Split('\n')[0].Split(": ", 2)[1]);
        var rawToken = Uri.UnescapeDataString(resetUrl.Query["?token=".Length..]);
        var expiredService = TestServiceFactory.CreateAuthService(
            context,
            new FixedTimeProvider(now.AddMinutes(6)),
            securityOptions,
            payloadProtector: protector);

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            expiredService.ResetPasswordAsync(new ResetPasswordRequest
            {
                Token = rawToken,
                NewPassword = "Changed@123"
            }));

        Assert.Equal("invalid_password_reset_token", exception.Code);
        var user = await context.Users.SingleAsync(candidate =>
            candidate.Id == registered.UserId);
        Assert.Equal(0, user.TokenVersion);
        Assert.All(
            await context.RefreshTokens
                .Where(token => token.UserId == registered.UserId)
                .ToListAsync(),
            token => Assert.False(token.IsRevoked));
        Assert.Null((await context.PasswordResetTokens.SingleAsync()).ConsumedAt);
    }

    private sealed class RecordingPasswordHasher : IPasswordHasher
    {
        private readonly BCryptPasswordHasher _inner = new();

        public List<(string Password, string? PasswordHash)> Verifications { get; } = [];

        public string Hash(string password) => _inner.Hash(password);

        public bool Verify(string password, string? passwordHash)
        {
            Verifications.Add((password, passwordHash));
            return _inner.Verify(password, passwordHash);
        }
    }

    private static async Task<(OutboxMessage Message, NotificationRequestedPayload Payload)>
        GetProtectedNotificationAsync(
            AppDbContext context,
            TestSensitivePayloadProtector protector,
            string subject)
    {
        var candidates = await context.OutboxMessages
            .Where(message =>
                message.Type == OutboxMessageTypes.ProtectedNotificationRequested)
            .ToListAsync();
        var matches = candidates
            .Select(message =>
            {
                var payload = JsonSerializer.Deserialize<NotificationRequestedPayload>(
                    protector.Unprotect(message.Payload),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                return (Message: message, Payload: payload);
            })
            .Where(candidate => candidate.Payload?.Subject == subject)
            .ToArray();
        var match = Assert.Single(matches);
        return (match.Message, Assert.IsType<NotificationRequestedPayload>(match.Payload));
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<string> Actions { get; } = [];

        public void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
            => Actions.Add(action);
    }
}
