using System.Net;
using System.Text;
using ECommerceBackend.API.Extensions;
using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class DeploymentSecurityTests
{
    [Fact]
    public void ProductionSecurity_RejectsInsecureDatabaseAndMissingHostAllowlist()
    {
        using var provider = CreateProvider(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=sql;Database=shop;Encrypt=False;TrustServerCertificate=True;",
                ["DataProtection:KeysPath"] = @"C:\keys"
            });

        var options = provider.GetRequiredService<IOptionsMonitor<ProductionSecurityOptions>>();
        Assert.Throws<OptionsValidationException>(() =>
            options.Get(ProductionSecurityOptions.OptionsName));
    }

    [Fact]
    public void ProductionSecurity_AcceptsExplicitTlsHostAndPersistentKeyPath()
    {
        using var provider = CreateProvider(
            Environments.Production,
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=sql.example.com;Database=shop;User Id=app;Password=secret;Encrypt=True;TrustServerCertificate=False;",
                ["AllowedHosts"] = "api.example.com",
                ["DataProtection:ApplicationName"] = "ECommerceBackend.Tests",
                ["DataProtection:KeysPath"] = @"C:\keys"
            });

        var production = provider
            .GetRequiredService<IOptionsMonitor<ProductionSecurityOptions>>()
            .Get(ProductionSecurityOptions.OptionsName);
        var dataProtection = provider
            .GetRequiredService<IOptions<DataProtectionStorageOptions>>()
            .Value;

        Assert.True(production.IsProduction);
        Assert.Equal("api.example.com", production.AllowedHosts);
        Assert.Equal(@"C:\keys", dataProtection.KeysPath);
    }

    [Fact]
    public void ReverseProxy_WhenEnabledWithoutTrustBoundary_FailsValidation()
    {
        using var provider = CreateProvider(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["ReverseProxy:Enabled"] = "true"
            });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ReverseProxyOptions>>().Value);
    }

    [Fact]
    public async Task ForwardedHeaders_TrustConfiguredProxyAndIgnoreUntrustedSource()
    {
        var values = new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = "true",
            ["ReverseProxy:KnownProxies:0"] = "10.0.0.10",
            ["ReverseProxy:ForwardLimit"] = "1"
        };
        using var provider = CreateProvider(Environments.Development, values);
        var options = provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>();

        var trusted = await InvokeForwardedHeadersAsync(
            options,
            IPAddress.Parse("10.0.0.10"));
        var untrusted = await InvokeForwardedHeadersAsync(
            options,
            IPAddress.Parse("10.0.0.11"));

        Assert.Equal("https", trusted.Scheme);
        Assert.Equal("203.0.113.25", trusted.RemoteIp);
        Assert.Equal("http", untrusted.Scheme);
        Assert.Equal("10.0.0.11", untrusted.RemoteIp);
    }

    [Fact]
    public async Task PublicHealthResponse_DoesNotExposeCheckDetails()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Healthy,
                    "Database connection is available.",
                    TimeSpan.FromMilliseconds(12),
                    exception: null,
                    data: new Dictionary<string, object> { ["server"] = "internal-sql" },
                    tags: ["ready"])
            },
            TimeSpan.FromMilliseconds(12));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthCheckResponseWriter.WritePublicAsync(context, report);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();

        Assert.Contains("Healthy", body);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internal-sql", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duration", body, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider CreateProvider(
        string environmentName,
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var environment = new TestWebHostEnvironment(Path.GetTempPath())
        {
            EnvironmentName = environmentName
        };
        var services = new ServiceCollection();
        services.AddECommerceConfigurationValidation(configuration, environment);
        services.AddECommerceReverseProxy(configuration);
        return services.BuildServiceProvider();
    }

    private static async Task<(string Scheme, string? RemoteIp)> InvokeForwardedHeadersAsync(
        IOptions<ForwardedHeadersOptions> options,
        IPAddress sourceAddress)
    {
        string? scheme = null;
        string? remoteIp = null;
        RequestDelegate next = context =>
        {
            scheme = context.Request.Scheme;
            remoteIp = context.Connection.RemoteIpAddress?.ToString();
            return Task.CompletedTask;
        };
        var middleware = new ForwardedHeadersMiddleware(
            next,
            NullLoggerFactory.Instance,
            options);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Connection.RemoteIpAddress = sourceAddress;
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.25";
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        await middleware.Invoke(context);
        return (scheme!, remoteIp);
    }
}
