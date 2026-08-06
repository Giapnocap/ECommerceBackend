using System.Net;
using ECommerceBackend.API.Health;
using ECommerceBackend.Application.Common;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Extensions
{
    public static partial class ServiceCollectionExtensions
    {
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

        public static IServiceCollection AddECommerceHealthChecks(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            var options = configuration
                .GetSection(HealthMonitoringOptions.SectionName)
                .Get<HealthMonitoringOptions>() ?? new HealthMonitoringOptions();
            var dependencyTimeout = TimeSpan.FromSeconds(
                options.DependencyTimeoutSeconds);

            services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy("Ứng dụng đang hoạt động."), tags: ["live"])
                .AddCheck<DatabaseHealthCheck>(
                    "database",
                    tags: ["ready"],
                    timeout: dependencyTimeout)
                .AddCheck<ProductImageStorageHealthCheck>(
                    "product-image-storage",
                    tags: ["ready"],
                    timeout: dependencyTimeout)
                .AddCheck<OutboxHealthCheck>(
                    "outbox",
                    tags: ["ready"],
                    timeout: dependencyTimeout)
                .AddCheck<OrderExpirationHealthCheck>(
                    "order-expiration",
                    tags: ["ready"],
                    timeout: dependencyTimeout)
                .AddCheck<DataRetentionHealthCheck>(
                    "data-retention",
                    tags: ["ready"],
                    timeout: dependencyTimeout);

            return services;
        }
    }
}
