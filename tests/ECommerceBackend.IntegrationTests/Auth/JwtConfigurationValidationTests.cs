using ECommerceBackend.API.Extensions;
using ECommerceBackend.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class JwtConfigurationValidationTests
{
    [Fact]
    public void JwtOptions_WithMissingKey_FailsValidation()
    {
        using var provider = CreateProvider(key: null);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<JwtOptions>>().Value);
    }

    [Fact]
    public void JwtOptions_WithAtLeast32Bytes_AreAccepted()
    {
        var key = new string('a', JwtOptions.MinimumKeyBytes);
        using var provider = CreateProvider(key);

        var options = provider.GetRequiredService<IOptions<JwtOptions>>().Value;

        Assert.Equal(key, options.Key);
    }

    [Fact]
    public void ProductionJwtPlaceholder_FailsValidation()
    {
        using var provider = CreateProvider(
            "replace-with-at-least-32-bytes-from-a-secret-store",
            Environments.Production);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<JwtOptions>>().Value);
    }

    [Fact]
    public void EnabledAdminBootstrap_WithPlaceholderPassword_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["AdminBootstrap:Enabled"] = "true",
            ["AdminBootstrap:UserName"] = "admin",
            ["AdminBootstrap:Email"] = "admin@example.com",
            ["AdminBootstrap:FullName"] = "Initial Admin",
            ["AdminBootstrap:Password"] = "replace-with-a-secure-password"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value);
    }

    [Fact]
    public void EnabledPaymentWebhook_WithPlaceholderSecret_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["PaymentWebhooks:GenericHmac:Enabled"] = "true",
            ["PaymentWebhooks:GenericHmac:ProviderCode"] = "generic-hmac",
            ["PaymentWebhooks:GenericHmac:Secret"] = "replace-with-at-least-32-bytes-from-a-secret-store"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PaymentWebhookOptions>>().Value);
    }

    [Fact]
    public void ProductionEnabledGenericHmacWebhook_WithStrongSecret_FailsValidation()
    {
        var values = EnabledGenericHmacWebhookValues();
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PaymentWebhookOptions>>().Value);
    }

    [Fact]
    public void DevelopmentEnabledGenericHmacWebhook_WithStrongSecret_IsAccepted()
    {
        var values = EnabledGenericHmacWebhookValues();
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Development,
            values);

        var options = provider
            .GetRequiredService<IOptions<PaymentWebhookOptions>>()
            .Value;

        Assert.True(options.Enabled);
        Assert.Equal("generic-hmac", options.ProviderCode);
    }

    [Fact]
    public void ProductionOutbox_WithoutSmtp_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Outbox:Enabled"] = "true",
            ["Notifications:Smtp:Enabled"] = "false"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<SmtpOptions>>().Value);
    }

    [Fact]
    public void ProductionWithoutOutbox_AllowsDisabledSmtp()
    {
        var values = new Dictionary<string, string?>
        {
            ["Outbox:Enabled"] = "false",
            ["Notifications:Smtp:Enabled"] = "false"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        Assert.False(provider.GetRequiredService<IOptions<SmtpOptions>>().Value.Enabled);
    }

    [Fact]
    public void OutboxProcessingTimeout_MustBeShorterThanLease()
    {
        var values = new Dictionary<string, string?>
        {
            ["Outbox:LockTimeoutMinutes"] = "1",
            ["Outbox:ProcessingTimeoutSeconds"] = "60"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<OutboxOptions>>().Value);
    }

    [Fact]
    public void RequiredOutboxProcessing_FailsWhenDispatcherIsDisabled()
    {
        var values = new Dictionary<string, string?>
        {
            ["Outbox:RequireProcessing"] = "true",
            ["Outbox:Enabled"] = "false"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<OutboxOptions>>().Value);
    }

    [Fact]
    public void DatabaseCommandTimeout_MustBeWithinSupportedRange()
    {
        var values = new Dictionary<string, string?>
        {
            ["Database:CommandTimeoutSeconds"] = "4"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    [Fact]
    public void HealthCheckDependencyTimeout_MustBeWithinSupportedRange()
    {
        var values = new Dictionary<string, string?>
        {
            ["HealthChecks:DependencyTimeoutSeconds"] = "31"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<HealthMonitoringOptions>>().Value);
    }

    [Fact]
    public void RequiredExpirationProcessing_FailsWhenWorkerIsDryRun()
    {
        var values = new Dictionary<string, string?>
        {
            ["OrderLifecycle:RequireExpirationProcessing"] = "true",
            ["OrderLifecycle:ExpirationEnabled"] = "true",
            ["OrderLifecycle:ExpirationDryRun"] = "true"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<OrderLifecycleOptions>>().Value);
    }

    [Fact]
    public void RequiredExpirationProcessing_FailsWhenWorkerIsDisabled()
    {
        var values = new Dictionary<string, string?>
        {
            ["OrderLifecycle:RequireExpirationProcessing"] = "true",
            ["OrderLifecycle:ExpirationEnabled"] = "false",
            ["OrderLifecycle:ExpirationDryRun"] = "false"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<OrderLifecycleOptions>>().Value);
    }

    [Fact]
    public void RequiredExpirationProcessing_AcceptsLiveWorker()
    {
        var values = new Dictionary<string, string?>
        {
            ["OrderLifecycle:RequireExpirationProcessing"] = "true",
            ["OrderLifecycle:ExpirationEnabled"] = "true",
            ["OrderLifecycle:ExpirationDryRun"] = "false"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.True(provider.GetRequiredService<IOptions<OrderLifecycleOptions>>().Value
            .RequireExpirationProcessing);
    }

    [Fact]
    public void AuthSecurity_WithInvalidLockoutPolicy_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["AuthSecurity:MaxFailedLoginAttempts"] = "1"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthSecurityOptions>>().Value);
    }

    [Fact]
    public void StripeEnabled_RequiresTestKeysAndWebhookSecret()
    {
        var values = new Dictionary<string, string?>
        {
            ["Payments:Stripe:Enabled"] = "true",
            ["Payments:Stripe:SecretKey"] = "live-secret",
            ["Payments:Stripe:PublishableKey"] = "public-key",
            ["Payments:Stripe:WebhookSecret"] = "webhook-secret"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<StripePaymentOptions>>()
                .Value);
    }

    [Fact]
    public void PaymentReconciliation_CannotRunWhenStripeIsDisabled()
    {
        var values = new Dictionary<string, string?>
        {
            ["Payments:Stripe:Enabled"] = "false",
            ["Payments:Stripe:ReconciliationEnabled"] = "true"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<StripePaymentOptions>>()
                .Value);
    }

    [Fact]
    public void Pricing_WithInvalidCurrencyOrTaxRate_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Pricing:Currency"] = "vnd",
            ["Pricing:TaxRatePercent"] = "101"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<PricingOptions>>()
                .Value);
    }

    [Fact]
    public void ExchangeRates_EnabledWithoutApiKey_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Pricing:ExchangeRates:Enabled"] = "true",
            ["Pricing:ExchangeRates:ApiKey"] = ""
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ExchangeRateOptions>>()
                .Value);
    }

    [Fact]
    public void ReturnPolicy_WithInvalidWindow_FailsValidation()
    {
        var values = new Dictionary<string, string?>
        {
            ["Returns:ReturnWindowDays"] = "0"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ReturnPolicyOptions>>()
                .Value);
    }

    [Theory]
    [InlineData("RateLimiting:Auth:PermitLimit", "0")]
    [InlineData("RateLimiting:Checkout:WindowSeconds", "3601")]
    public void RateLimiting_WithInvalidPolicy_FailsValidation(
        string key,
        string value)
    {
        var values = new Dictionary<string, string?>
        {
            [key] = value
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            additionalValues: values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<RateLimitingOptions>>()
                .Value);
    }

    [Fact]
    public void ProductionPasswordResetUrl_MustUsePublicHttps()
    {
        var values = new Dictionary<string, string?>
        {
            ["AuthSecurity:PasswordResetUrl"] = "http://localhost:3000/reset-password",
            ["AuthSecurity:EmailVerificationUrl"] = "https://shop.example.com/verify-email"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthSecurityOptions>>().Value);
    }

    [Fact]
    public void ProductionPasswordResetUrl_AcceptsPublicHttpsWithoutQuery()
    {
        var values = new Dictionary<string, string?>
        {
            ["AuthSecurity:PasswordResetUrl"] = "https://shop.example.com/reset-password",
            ["AuthSecurity:EmailVerificationUrl"] = "https://shop.example.com/verify-email"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        var options = provider
            .GetRequiredService<IOptions<AuthSecurityOptions>>()
            .Value;

        Assert.Equal(
            "https://shop.example.com/reset-password",
            options.PasswordResetUrl);
    }

    [Fact]
    public void ProductionEmailVerificationUrl_MustUsePublicHttps()
    {
        var values = new Dictionary<string, string?>
        {
            ["AuthSecurity:PasswordResetUrl"] = "https://shop.example.com/reset-password",
            ["AuthSecurity:EmailVerificationUrl"] = "http://localhost:3000/verify-email"
        };
        using var provider = CreateProvider(
            new string('a', JwtOptions.MinimumKeyBytes),
            Environments.Production,
            values);

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AuthSecurityOptions>>().Value);
    }

    private static ServiceProvider CreateProvider(
        string? key,
        string environmentName = "Development",
        IReadOnlyDictionary<string, string?>? additionalValues = null)
    {
        var values = additionalValues is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(additionalValues);
        if (key is not null)
            values["Jwt:Key"] = key;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        var environment = new TestWebHostEnvironment
        {
            EnvironmentName = environmentName
        };

        services.AddECommerceConfigurationValidation(configuration, environment);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> EnabledGenericHmacWebhookValues()
        => new()
        {
            ["PaymentWebhooks:GenericHmac:Enabled"] = "true",
            ["PaymentWebhooks:GenericHmac:ProviderCode"] = "generic-hmac",
            ["PaymentWebhooks:GenericHmac:Secret"] = new string(
                's',
                PaymentWebhookOptions.MinimumSecretBytes)
        };

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "ECommerceBackend.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
