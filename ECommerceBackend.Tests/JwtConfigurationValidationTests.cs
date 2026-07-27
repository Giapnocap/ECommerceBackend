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
    public void ProductionPasswordResetUrl_MustUsePublicHttps()
    {
        var values = new Dictionary<string, string?>
        {
            ["AuthSecurity:PasswordResetUrl"] = "http://localhost:3000/reset-password"
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
            ["AuthSecurity:PasswordResetUrl"] = "https://shop.example.com/reset-password"
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
