using System.Net;
using System.Text;
using ECommerceBackend.Application.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;

namespace ECommerceBackend.API.Extensions
{
    public static partial class ServiceCollectionExtensions
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
                .Validate(
                    options => IsValidJwtOptions(options, environment),
                    "Jwt config is invalid or contains a production placeholder secret.")
                .ValidateOnStart();

            services.AddOptions<AuthSecurityOptions>()
                .Bind(configuration.GetSection(AuthSecurityOptions.SectionName))
                .Validate(
                    options => IsValidAuthSecurityOptions(options, environment),
                    "Auth security config is invalid.")
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

            services.AddOptions<DatabaseOptions>()
                .Bind(configuration.GetSection(DatabaseOptions.SectionName))
                .Validate(IsValidDatabaseOptions, "Database config is invalid.")
                .ValidateOnStart();

            services.AddOptions<DataRetentionOptions>()
                .Bind(configuration.GetSection(DataRetentionOptions.SectionName))
                .Validate(IsValidDataRetentionOptions, "Data retention config is invalid.")
                .ValidateOnStart();

            services.AddOptions<OrderLifecycleOptions>()
                .Bind(configuration.GetSection(OrderLifecycleOptions.SectionName))
                .Validate(IsValidOrderLifecycleOptions, "Order lifecycle config is invalid.")
                .ValidateOnStart();

            services.AddOptions<PricingOptions>()
                .Bind(configuration.GetSection(PricingOptions.SectionName))
                .Validate(
                    IsValidPricingOptions,
                    "Pricing config is invalid.")
                .ValidateOnStart();

            services.AddOptions<ReturnPolicyOptions>()
                .Bind(configuration.GetSection(ReturnPolicyOptions.SectionName))
                .Validate(
                    options => options.ReturnWindowDays is >= 1 and <= 90,
                    "Return policy config is invalid.")
                .ValidateOnStart();

            services.AddOptions<SmtpOptions>()
                .Bind(configuration.GetSection(SmtpOptions.SectionName))
                .Validate(
                    options => IsValidSmtpOptions(options, configuredOutbox, environment),
                    "SMTP config is invalid. Production outbox delivery requires enabled SMTP.")
                .ValidateOnStart();

            return services;
        }

        private static bool IsValidPricingOptions(
            PricingOptions options)
            => options.Currency is { Length: 3 }
                && options.Currency.All(
                    character => character is >= 'A' and <= 'Z')
                && options.QuoteValidityMinutes is >= 1 and <= 60
                && IsValidMoney(options.StandardShippingFee)
                && IsValidMoney(options.ExpressShippingFee)
                && IsValidMoney(
                    options.FreeStandardShippingMinimum)
                && IsValidMoney(options.TaxRatePercent)
                && options.TaxRatePercent <= 100;

        private static bool IsValidMoney(decimal value)
            => value >= 0
                && value <= CommerceLimits.MaxMoneyAmount
                && decimal.Round(
                    value,
                    CommerceLimits.MoneyScale,
                    MidpointRounding.ToEven) == value;

        private static bool IsValidJwtOptions(
            JwtOptions options,
            IWebHostEnvironment environment)
            => HasUsableJwtOptions(options)
                && (!environment.IsProduction() || !LooksLikePlaceholder(options.Key));

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

        private static bool IsValidAuthSecurityOptions(
            AuthSecurityOptions options,
            IWebHostEnvironment environment)
        {
            if (options.MaxFailedLoginAttempts is < 2 or > 20
                || options.LockoutMinutes is < 1 or > 1440
                || options.PasswordResetTokenMinutes is < 5 or > 1440
                || !Uri.TryCreate(
                    options.PasswordResetUrl?.Trim(),
                    UriKind.Absolute,
                    out var resetUrl)
                || resetUrl.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(resetUrl.UserInfo)
                || !string.IsNullOrEmpty(resetUrl.Query)
                || !string.IsNullOrEmpty(resetUrl.Fragment))
            {
                return false;
            }

            return !environment.IsProduction()
                || (resetUrl.Scheme == "https"
                    && !string.Equals(
                        resetUrl.Host,
                        "localhost",
                        StringComparison.OrdinalIgnoreCase));
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
                && options.Password.Length is >= 12 and <= 128
                && !LooksLikePlaceholder(options.Password);
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
                || (Encoding.UTF8.GetByteCount(options.Secret) >= PaymentWebhookOptions.MinimumSecretBytes
                    && !LooksLikePlaceholder(options.Secret));
        }

        private static bool IsValidOutboxOptions(OutboxOptions options)
            => (!options.RequireProcessing || options.Enabled)
                && options.PollIntervalSeconds is >= 1 and <= 300
                && options.BatchSize is >= 1 and <= 500
                && options.MaxAttempts is >= 1 and <= 100
                && options.LockTimeoutMinutes is >= 1 and <= 1440
                && options.ProcessingTimeoutSeconds is >= 5 and <= 300
                && options.MaxPendingAgeMinutes is >= 1 and <= 1440
                && options.LockTimeoutMinutes * 60 > options.ProcessingTimeoutSeconds;

        private static bool IsValidDatabaseOptions(DatabaseOptions options)
            => options.CommandTimeoutSeconds is >= 5 and <= 300;

        private static bool IsValidDataRetentionOptions(DataRetentionOptions options)
            => (!options.RequireAutomaticProcessing || options.AutomaticProcessingEnabled)
                && (!options.AutomaticProcessingEnabled || options.Enabled)
                && options.ProcessedOutboxRetentionDays is >= 1 and <= 3650
                && options.ExpiredRefreshTokenRetentionDays is >= 1 and <= 3650
                && options.WebhookPayloadRetentionDays is >= 1 and <= 3650
                && options.MaxBatchSize is >= 1 and <= 500
                && options.ProcessingIntervalMinutes is >= 5 and <= 10080
                && options.FailureRetryMinutes is >= 1 and <= 1440
                && options.MaxBatchesPerCycle is >= 1 and <= 100;

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
                && knownNetworks.All(network =>
                    TryParseNetwork(network, out var parsedNetwork)
                    && parsedNetwork.PrefixLength > 0);
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

        private static bool LooksLikePlaceholder(string value)
        {
            var normalized = value.Trim().ToLowerInvariant();
            return normalized.StartsWith("replace-with", StringComparison.Ordinal)
                || normalized.StartsWith("change-me", StringComparison.Ordinal)
                || normalized.StartsWith("changeme", StringComparison.Ordinal)
                || normalized.Contains("placeholder", StringComparison.Ordinal)
                || normalized.StartsWith('<') && normalized.EndsWith('>');
        }

        private static bool IsValidOrderLifecycleOptions(OrderLifecycleOptions options)
            => options.PendingCodHoldMinutes is >= 1 and <= 1440
                && options.MaxPendingOrdersPerCustomer is >= 1 and <= 100
                && options.ExpirationPollIntervalSeconds is >= 1 and <= 300
                && options.ExpirationBatchSize is >= 1 and <= 500
                && options.MaxOverdueMinutes is >= 1 and <= 1440
                && (!options.RequireExpirationProcessing
                    || options.ExpirationEnabled && !options.ExpirationDryRun);

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

    }
}
