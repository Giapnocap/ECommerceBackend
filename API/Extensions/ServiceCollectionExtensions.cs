using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Net;
using System.Threading.RateLimiting;
using ECommerceBackend.API.Errors;
using ECommerceBackend.API.Health;
using ECommerceBackend.API.Swagger;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Application.Validation;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Orders;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Infrastructure.Repositories;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Data.SqlClient;
using Microsoft.OpenApi.Models;

namespace ECommerceBackend.API.Extensions
{
    public static class ServiceCollectionExtensions
    {
        private static readonly string[] DevelopmentCorsOrigins =
        [
            "http://localhost:3000"
        ];

        public static IServiceCollection AddECommerceDataProtection(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var options = configuration
                .GetSection(DataProtectionStorageOptions.SectionName)
                .Get<DataProtectionStorageOptions>() ?? new DataProtectionStorageOptions();
            var configuredPath = options.KeysPath?.Trim();
            var dataProtectionPath = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(environment.ContentRootPath, "DataProtectionKeys")
                : Path.GetFullPath(configuredPath, environment.ContentRootPath);
            Directory.CreateDirectory(dataProtectionPath);

            services.AddDataProtection()
                .SetApplicationName(options.ApplicationName.Trim())
                .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath));

            return services;
        }

        public static IServiceCollection AddECommerceConfigurationValidation(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var configuredOutbox = configuration
                .GetSection(OutboxOptions.SectionName)
                .Get<OutboxOptions>() ?? new OutboxOptions();

            services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.SectionName))
                .Validate(IsValidJwtOptions, "Jwt config is invalid.")
                .ValidateOnStart();

            services.AddOptions<ReverseProxyOptions>()
                .Bind(configuration.GetSection(ReverseProxyOptions.SectionName))
                .Validate(IsValidReverseProxyOptions, "Reverse proxy config is invalid or has no trusted proxy/network.")
                .ValidateOnStart();

            services.AddOptions<DataProtectionStorageOptions>()
                .Bind(configuration.GetSection(DataProtectionStorageOptions.SectionName))
                .Validate(
                    options => IsValidDataProtectionOptions(options, environment),
                    "Production requires a non-empty application name and an absolute DataProtection keys path.")
                .ValidateOnStart();

            services.AddOptions<ProductionSecurityOptions>(ProductionSecurityOptions.OptionsName)
                .Configure(options =>
                {
                    options.ConnectionString = configuration.GetConnectionString("Default") ?? string.Empty;
                    options.AllowedHosts = configuration["AllowedHosts"] ?? string.Empty;
                    options.IsProduction = environment.IsProduction();
                })
                .Validate(IsValidProductionSecurityOptions, "Production database TLS or AllowedHosts config is insecure.")
                .ValidateOnStart();

            services.AddOptions<CorsOptions>()
                .Bind(configuration.GetSection(CorsOptions.SectionName))
                .Validate(options => IsValidCorsOptions(options, environment), "Cors config is invalid.")
                .ValidateOnStart();

            services.AddOptions<AutoMapperOptions>()
                .Bind(configuration.GetSection(AutoMapperOptions.SectionName));

            services.AddOptions<AdminBootstrapOptions>()
                .Bind(configuration.GetSection(AdminBootstrapOptions.SectionName))
                .Validate(IsValidAdminBootstrapOptions, "Admin bootstrap config is invalid.")
                .ValidateOnStart();

            services.AddOptions<PaymentWebhookOptions>()
                .Bind(configuration.GetSection(PaymentWebhookOptions.SectionName))
                .Validate(IsValidPaymentWebhookOptions, "Payment webhook config is invalid.")
                .ValidateOnStart();

            services.AddOptions<OutboxOptions>()
                .Bind(configuration.GetSection(OutboxOptions.SectionName))
                .Validate(IsValidOutboxOptions, "Outbox config is invalid.")
                .ValidateOnStart();

            services.AddOptions<OrderLifecycleOptions>()
                .Bind(configuration.GetSection(OrderLifecycleOptions.SectionName))
                .Validate(IsValidOrderLifecycleOptions, "Order lifecycle config is invalid.")
                .ValidateOnStart();

            services.AddOptions<SmtpOptions>()
                .Bind(configuration.GetSection(SmtpOptions.SectionName))
                .Validate(
                    options => IsValidSmtpOptions(options, configuredOutbox, environment),
                    "SMTP config is invalid. Production outbox delivery requires enabled SMTP.")
                .ValidateOnStart();

            return services;
        }

        public static IServiceCollection AddECommerceReverseProxy(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var proxyOptions = configuration
                .GetSection(ReverseProxyOptions.SectionName)
                .Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();

            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = proxyOptions.Enabled
                    ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
                    : ForwardedHeaders.None;
                options.ForwardLimit = proxyOptions.ForwardLimit;
                options.RequireHeaderSymmetry = proxyOptions.RequireHeaderSymmetry;
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();

                foreach (var proxy in proxyOptions.KnownProxies)
                {
                    if (IPAddress.TryParse(proxy, out var address))
                        options.KnownProxies.Add(address);
                }

                foreach (var network in proxyOptions.KnownNetworks)
                {
                    if (TryParseNetwork(network, out var parsedNetwork))
                        options.KnownNetworks.Add(parsedNetwork);
                }
            });

            return services;
        }

        public static IServiceCollection AddECommerceControllers(this IServiceCollection services)
        {
            services.AddControllers();
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(
                            e => e.Key,
                            e => e.Value!.Errors.Select(err =>
                                string.IsNullOrWhiteSpace(err.ErrorMessage)
                                    ? "Giá trị không hợp lệ."
                                    : err.ErrorMessage).ToArray());

                    return new ApiProblemDetailsResult(ApiProblemDetails.Create(
                        context.HttpContext,
                        StatusCodes.Status400BadRequest,
                        "validation_error",
                        "Dữ liệu gửi lên không hợp lệ.",
                        errors));
                };
            });

            return services;
        }

        public static IServiceCollection AddECommerceValidation(this IServiceCollection services)
        {
            services.AddFluentValidationAutoValidation();
            services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
            return services;
        }

        public static IServiceCollection AddECommerceMapping(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var licenseKey = configuration
                .GetSection(AutoMapperOptions.SectionName)
                .GetValue<string?>(nameof(AutoMapperOptions.LicenseKey));

            services.AddAutoMapper(config =>
            {
                if (!string.IsNullOrWhiteSpace(licenseKey))
                    config.LicenseKey = licenseKey;

                config.AddProfile<MappingProfile>();
            });

            return services;
        }

        public static IServiceCollection AddECommerceDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("Default")));
            services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
            services.AddScoped<IDataConsistencyService, EfDataConsistencyService>();

            return services;
        }

        public static IServiceCollection AddECommerceRepositoriesAndServices(this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddHttpContextAccessor();
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICategoryService, CategoryService>();
            services.AddScoped<IProductService, ProductService>();
            services.AddScoped<IUploadService, UploadService>();
            services.AddScoped<ICartService, CartService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<IOutboxWriter, OutboxWriter>();
            services.AddScoped<IAuditWriter, AuditWriter>();
            services.AddScoped<IOperationsService, OperationsService>();
            services.AddScoped<IUploadReconciliationService, UploadReconciliationService>();
            services.AddScoped<IPaymentWebhookService, PaymentWebhookService>();
            services.AddScoped<IOutboxStore, EfOutboxStore>();
            services.AddScoped<IOutboxMessageHandler, NotificationOutboxMessageHandler>();
            services.AddScoped<INotificationSender, ConfigurableNotificationSender>();
            services.AddScoped<OutboxProcessor>();
            services.AddSingleton<IPaymentProvider, CashOnDeliveryPaymentProvider>();
            services.AddSingleton<IPaymentProvider, GenericHmacPaymentProvider>();
            services.AddSingleton<IPaymentProviderResolver, PaymentProviderResolver>();
            services.AddScoped<AdminBootstrapper>();
            services.AddHostedService<AdminBootstrapHostedService>();
            services.AddHostedService<OutboxDispatcherHostedService>();
            services.AddHostedService<OrderExpirationHostedService>();

            return services;
        }

        public static WebApplicationBuilder ConfigureECommerceUploadLimits(this WebApplicationBuilder builder)
        {
            var uploadSection = builder.Configuration.GetSection(UploadOptions.SectionName);
            var maxImageSizeBytes = uploadSection.GetValue<long?>(nameof(UploadOptions.MaxImageSizeBytes))
                ?? UploadOptions.DefaultMaxImageSizeBytes;

            if (maxImageSizeBytes <= 0)
                throw new InvalidOperationException("Uploads:MaxImageSizeBytes must be greater than zero.");

            builder.Services.AddOptions<UploadOptions>()
                .Bind(uploadSection)
                .Validate(options => options.MaxImageSizeBytes > 0
                    && options.ReconciliationGraceMinutes is >= 1 and <= 10080
                    && options.MaxReconciliationDeletes is >= 1 and <= 1000,
                    "Upload limits or reconciliation config is invalid.")
                .ValidateOnStart();

            var maxRequestBodySize = checked(maxImageSizeBytes + 1024 * 1024);
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = maxRequestBodySize;
            });

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.MaxRequestBodySize = maxRequestBodySize;
            });

            return builder;
        }

        public static IServiceCollection AddECommerceCors(
            this IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var allowedOrigins = ResolveCorsOrigins(configuration, environment);

            services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins(allowedOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            return services;
        }

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
                                "Access token is missing, invalid, expired, or no longer active.");
                        },
                        OnForbidden = context => WriteAuthenticationErrorAsync(
                            context.HttpContext,
                            StatusCodes.Status403Forbidden,
                            "forbidden",
                            "You do not have permission to perform this action.")
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

        public static IServiceCollection AddECommerceHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy("Application is running."), tags: ["live"])
                .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
                .AddCheck<OutboxHealthCheck>("outbox", tags: ["ready"])
                .AddCheck<OrderExpirationHealthCheck>("order-expiration", tags: ["ready"]);

            return services;
        }

        public static IServiceCollection AddECommerceSwagger(this IServiceCollection services)
        {
            services.AddSwaggerGen(options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title = "ECommerce API",
                    Version = "v1",
                    Description = "E-Commerce Backend API với Clean Architecture - ASP.NET Core 8, JWT Auth, EF Core, SQL Server."
                });

                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name = "Authorization",
                    Type = SecuritySchemeType.Http,
                    Scheme = "Bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Nhập token dạng: Bearer {token}"
                });

                options.OperationFilter<AuthorizeOperationFilter>();
                options.OperationFilter<DefaultResponseOperationFilter>();

                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                    options.IncludeXmlComments(xmlPath);
            });

            return services;
        }

        private static bool IsValidJwtOptions(JwtOptions options)
            => HasUsableJwtOptions(options);

        private static bool HasUsableJwtOptions(JwtOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.Key))
                return false;

            if (Encoding.UTF8.GetByteCount(options.Key) < JwtOptions.MinimumKeyBytes)
                return false;

            if (string.IsNullOrWhiteSpace(options.Issuer) || string.IsNullOrWhiteSpace(options.Audience))
                return false;

            if (options.AccessTokenMinutes <= 0 || options.RefreshTokenDays <= 0)
                return false;

            return true;
        }

        private static bool IsValidCorsOptions(CorsOptions options, IWebHostEnvironment environment)
        {
            var origins = options.AllowedOrigins ?? [];
            if (origins.Length == 0)
                return environment.IsDevelopment();

            return origins.All(IsValidCorsOrigin)
                && (environment.IsDevelopment() || origins.All(origin => !IsLocalhostOrigin(origin)));
        }

        private static string[] ResolveCorsOrigins(
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            var configuredOrigins = configuration
                .GetSection(CorsOptions.SectionName)
                .Get<CorsOptions>()?
                .AllowedOrigins ?? [];

            var origins = configuredOrigins.Length == 0 && environment.IsDevelopment()
                ? DevelopmentCorsOrigins
                : configuredOrigins;

            var normalizedOrigins = origins
                .Select(origin => origin.Trim().TrimEnd('/'))
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedOrigins.Length == 0)
                throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one origin.");

            if (normalizedOrigins.Any(origin => !IsValidCorsOrigin(origin)))
                throw new InvalidOperationException("Cors:AllowedOrigins contains an invalid origin.");

            if (!environment.IsDevelopment() && normalizedOrigins.Any(IsLocalhostOrigin))
                throw new InvalidOperationException("Cors:AllowedOrigins must not use localhost outside Development.");

            return normalizedOrigins;
        }

        private static bool IsValidCorsOrigin(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin) || origin == "*")
                return false;

            if (!Uri.TryCreate(origin.Trim().TrimEnd('/'), UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme is not ("http" or "https"))
                return false;

            return string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment)
                && uri.AbsolutePath == "/";
        }

        private static bool IsLocalhostOrigin(string origin)
        {
            if (!Uri.TryCreate(origin.Trim().TrimEnd('/'), UriKind.Absolute, out var uri))
                return false;

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsValidAdminBootstrapOptions(AdminBootstrapOptions options)
        {
            if (!options.Enabled)
                return true;

            return !string.IsNullOrWhiteSpace(options.UserName)
                && options.UserName.Length is >= 3 and <= 50
                && !string.IsNullOrWhiteSpace(options.Email)
                && options.Email.Length <= 254
                && !string.IsNullOrWhiteSpace(options.FullName)
                && options.FullName.Length <= 100
                && options.Password.Length is >= 12 and <= 128;
        }

        private static bool IsValidPaymentWebhookOptions(PaymentWebhookOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ProviderCode)
                || options.ProviderCode.Length > 100
                || options.ProviderCode.Any(character => !char.IsLetterOrDigit(character)
                    && character is not '-' and not '_'))
            {
                return false;
            }

            if (options.MaxPayloadBytes is < 1024 or > 1_048_576
                || options.MaxFutureSkewMinutes is < 0 or > 60)
            {
                return false;
            }

            return !options.Enabled
                || Encoding.UTF8.GetByteCount(options.Secret) >= PaymentWebhookOptions.MinimumSecretBytes;
        }

        private static bool IsValidOutboxOptions(OutboxOptions options)
            => options.PollIntervalSeconds is >= 1 and <= 300
                && options.BatchSize is >= 1 and <= 500
                && options.MaxAttempts is >= 1 and <= 100
                && options.LockTimeoutMinutes is >= 1 and <= 1440
                && options.ProcessingTimeoutSeconds is >= 5 and <= 300
                && options.MaxPendingAgeMinutes is >= 1 and <= 1440
                && options.LockTimeoutMinutes * 60 > options.ProcessingTimeoutSeconds;

        private static bool IsValidReverseProxyOptions(ReverseProxyOptions options)
        {
            if (options.ForwardLimit is < 1 or > 5)
                return false;

            if (!options.Enabled)
                return true;

            var knownProxies = options.KnownProxies ?? [];
            var knownNetworks = options.KnownNetworks ?? [];
            return knownProxies.Length + knownNetworks.Length > 0
                && knownProxies.All(proxy => IPAddress.TryParse(proxy, out _))
                && knownNetworks.All(network => TryParseNetwork(network, out _));
        }

        private static bool IsValidDataProtectionOptions(
            DataProtectionStorageOptions options,
            IWebHostEnvironment environment)
        {
            if (string.IsNullOrWhiteSpace(options.ApplicationName)
                || options.ApplicationName.Trim().Length > 100)
            {
                return false;
            }

            return !environment.IsProduction()
                || !string.IsNullOrWhiteSpace(options.KeysPath)
                    && Path.IsPathFullyQualified(options.KeysPath.Trim());
        }

        private static bool IsValidProductionSecurityOptions(ProductionSecurityOptions options)
        {
            if (!options.IsProduction)
                return true;

            if (string.IsNullOrWhiteSpace(options.ConnectionString)
                || options.ConnectionString.Contains("YOUR_", StringComparison.OrdinalIgnoreCase)
                || options.ConnectionString.Contains("...", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                var connection = new SqlConnectionStringBuilder(options.ConnectionString);
                if (string.IsNullOrWhiteSpace(connection.DataSource)
                    || string.IsNullOrWhiteSpace(connection.InitialCatalog)
                    || !TryGetConnectionBoolean(options.ConnectionString, "Encrypt", out var encrypt)
                    || !encrypt
                    || !TryGetConnectionBoolean(options.ConnectionString, "TrustServerCertificate", out var trustCertificate)
                    || trustCertificate)
                {
                    return false;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            var allowedHosts = options.AllowedHosts
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return allowedHosts.Length > 0
                && allowedHosts.All(IsValidAllowedHost)
                && allowedHosts.All(host => !IsLocalHost(host));
        }

        private static bool TryParseNetwork(
            string value,
            out Microsoft.AspNetCore.HttpOverrides.IPNetwork network)
        {
            network = default!;
            var parts = value.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var prefixLength))
            {
                return false;
            }

            var maximumPrefixLength = prefix.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                ? 32
                : 128;
            if (prefixLength < 0 || prefixLength > maximumPrefixLength)
                return false;

            network = new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
            return true;
        }

        private static bool TryGetConnectionBoolean(
            string connectionString,
            string key,
            out bool value)
        {
            value = false;
            var segment = connectionString
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.Split('=', 2, StringSplitOptions.TrimEntries))
                .FirstOrDefault(parts => parts.Length == 2
                    && string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase));
            return segment is { Length: 2 } && bool.TryParse(segment[1], out value);
        }

        private static bool IsValidAllowedHost(string host)
        {
            if (host == "*"
                || host.Contains('*')
                || host.Contains("//", StringComparison.Ordinal)
                || host.Contains('/')
                || host.Contains('\\'))
            {
                return false;
            }

            return IPAddress.TryParse(host, out _)
                || Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
        }

        private static bool IsLocalHost(string host)
            => string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase);

        private static bool IsValidOrderLifecycleOptions(OrderLifecycleOptions options)
            => options.PendingCodHoldMinutes is >= 1 and <= 1440
                && options.MaxPendingOrdersPerCustomer is >= 1 and <= 100
                && options.ExpirationPollIntervalSeconds is >= 1 and <= 300
                && options.ExpirationBatchSize is >= 1 and <= 500
                && options.MaxOverdueMinutes is >= 1 and <= 1440;

        private static bool IsValidSmtpOptions(
            SmtpOptions options,
            OutboxOptions outbox,
            IWebHostEnvironment environment)
        {
            if (options.TimeoutSeconds is < 1 or > 300
                || options.TimeoutSeconds > outbox.ProcessingTimeoutSeconds)
            {
                return false;
            }

            if (!options.Enabled)
                return environment.IsDevelopment()
                    || environment.IsEnvironment("Testing")
                    || !outbox.Enabled;

            if (string.IsNullOrWhiteSpace(options.Host)
                || options.Port is < 1 or > 65535
                || string.IsNullOrWhiteSpace(options.FromAddress)
                || string.IsNullOrWhiteSpace(options.FromName)
                || (!string.IsNullOrWhiteSpace(options.UserName)
                    && string.IsNullOrWhiteSpace(options.Password)))
            {
                return false;
            }

            try
            {
                _ = new System.Net.Mail.MailAddress(options.FromAddress);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
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
                context.Fail("Token session claims are invalid.");
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
                context.Fail("Token session is no longer active.");
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
