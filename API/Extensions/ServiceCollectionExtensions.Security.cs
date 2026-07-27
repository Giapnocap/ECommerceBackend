using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using ECommerceBackend.API.Errors;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceBackend.API.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddECommerceJwtAuthentication(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                ?? new JwtOptions();

            if (!HasUsableJwtOptions(jwtOptions))
                throw new InvalidOperationException("Jwt config is invalid.");

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.RequireHttpsMetadata = !environment.IsDevelopment();
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtOptions.Issuer,
                        ValidateAudience = true,
                        ValidAudience = jwtOptions.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ClockSkew = TimeSpan.FromMinutes(1),
                        NameClaimType = ClaimTypes.Name,
                        RoleClaimType = ClaimTypes.Role,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtOptions.Key))
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = ValidateSessionAsync,
                        OnChallenge = async context =>
                        {
                            context.HandleResponse();
                            await WriteAuthenticationErrorAsync(
                                context.HttpContext,
                                StatusCodes.Status401Unauthorized,
                                "unauthorized",
                                "Mã truy cập bị thiếu, không hợp lệ, đã hết hạn hoặc không còn hiệu lực.");
                        },
                        OnForbidden = context => WriteAuthenticationErrorAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "forbidden",
                            "Bạn không có quyền thực hiện thao tác này.")
                    };
                });

            return services;
        }

        public static IServiceCollection AddECommerceAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.AddPolicy(AuthorizationPolicyNames.CustomerAccess, policy =>
                    policy.RequireRole(RoleNames.Customer));

                foreach (var permission in PermissionNames.All)
                {
                    options.AddPolicy(permission, policy =>
                        policy.RequireClaim(AuthClaimTypes.Permission, permission));
                }
            });

            return services;
        }

        public static IServiceCollection AddECommerceRateLimiting(this IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    await ApiProblemDetails.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status429TooManyRequests,
                        "rate_limit_exceeded",
                        "Quá nhiều yêu cầu. Vui lòng thử lại sau.",
                        cancellationToken: cancellationToken);
                };

                options.AddPolicy("auth", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 10,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

                options.AddPolicy("refresh", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

                options.AddPolicy("upload", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

                options.AddPolicy("webhook", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 120,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));

                options.AddPolicy("checkout", httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        GetRateLimitPartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 5,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        }));
            });

            return services;
        }

        private static async Task ValidateSessionAsync(TokenValidatedContext context)
        {
            var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var tokenVersionValue = context.Principal?.FindFirstValue(AuthClaimTypes.TokenVersion);
            var sessionIdValue = context.Principal?.FindFirstValue(AuthClaimTypes.SessionId);

            if (!Guid.TryParse(userIdValue, out var userId)
                || !int.TryParse(tokenVersionValue, out var tokenVersion)
                || !Guid.TryParse(sessionIdValue, out var sessionId))
            {
                context.Fail("Thông tin phiên đăng nhập trong mã xác thực không hợp lệ.");
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var timeProvider = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var session = await dbContext.Users
                .AsNoTracking()
                .Where(user => user.Id == userId && !user.IsDeleted)
                .Select(user => new
                {
                    user.TokenVersion,
                    HasActiveSession = user.RefreshTokens.Any(token => token.FamilyId == sessionId
                        && token.RevokedAt == null
                        && token.ExpiresAt > now)
                })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

            if (session == null || session.TokenVersion != tokenVersion || !session.HasActiveSession)
                context.Fail("Phiên đăng nhập không còn hiệu lực.");
        }

        private static string GetRateLimitPartitionKey(HttpContext context)
            => context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

        private static Task WriteAuthenticationErrorAsync(
            HttpContext context,
            int statusCode,
            string code,
            string message)
        {
            if (context.Response.HasStarted)
                return Task.CompletedTask;

            return ApiProblemDetails.WriteAsync(
                context,
                statusCode,
                code,
                message,
                cancellationToken: context.RequestAborted);
        }
    }
}
