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