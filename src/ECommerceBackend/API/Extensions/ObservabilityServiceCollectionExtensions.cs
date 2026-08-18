using System.Diagnostics;
using System.Reflection;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Observability;
using ECommerceBackend.Infrastructure.Observability;
using ECommerceBackend.Infrastructure.Payments;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ECommerceBackend.API.Extensions
{
    public static class ObservabilityServiceCollectionExtensions
    {
        private static readonly string[] ApplicationMeters =
        [
            BusinessTelemetry.MeterName,
            DatabaseTelemetryInterceptor.MeterName,
            "ECommerceBackend.Auth",
            "ECommerceBackend.Catalog",
            "ECommerceBackend.Operations",
            "ECommerceBackend.OrderExpiration",
            PaymentReconciliationHostedService.MeterName,
            "ECommerceBackend.Outbox"
        ];

        public static IServiceCollection AddECommerceObservability(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
        {
            var section = configuration.GetSection(ObservabilityOptions.SectionName);
            var options = section.Get<ObservabilityOptions>() ?? new ObservabilityOptions();

            services.AddOptions<ObservabilityOptions>()
                .Bind(section)
                .Validate(IsValid, "Observability config is invalid.")
                .ValidateOnStart();
            services.TryAddSingleton<DatabaseTelemetryInterceptor>();

            if (!options.Enabled || !IsValid(options))
                return services;

            var version = Assembly.GetExecutingAssembly()
                .GetName()
                .Version?
                .ToString() ?? "unknown";
            services.AddOpenTelemetry()
                .ConfigureResource(builder => builder
                    .AddService(options.ServiceName.Trim(), serviceVersion: version)
                    .AddAttributes(
                    [
                        new KeyValuePair<string, object>(
                            "deployment.environment.name",
                            environment.EnvironmentName)
                    ]))
                .WithTracing(builder =>
                {
                    builder
                        .SetSampler(new ParentBasedSampler(
                            new TraceIdRatioBasedSampler(options.TraceSamplingRatio)))
                        .AddSource(
                            BusinessTelemetry.ActivitySourceName,
                            "ECommerceBackend.Operations")
                        .AddAspNetCoreInstrumentation(instrumentation =>
                        {
                            instrumentation.RecordException = false;
                            instrumentation.Filter = context =>
                                !context.Request.Path.StartsWithSegments("/health");
                            instrumentation.EnrichWithHttpRequest = static (activity, _) =>
                            {
                                activity.SetTag("url.query", null);
                                activity.SetTag("http.target", null);
                            };
                        })
                        .AddHttpClientInstrumentation(instrumentation =>
                        {
                            instrumentation.RecordException = false;
                            instrumentation.EnrichWithHttpRequestMessage =
                                static (activity, request) => RedactClientUrl(activity, request);
                        });

                    if (options.Otlp?.Enabled == true)
                    {
                        builder.AddOtlpExporter(exporter =>
                            exporter.Endpoint = new Uri(options.Otlp.Endpoint.Trim()));
                    }
                })
                .WithMetrics(builder =>
                {
                    builder
                        .AddMeter(ApplicationMeters)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();

                    if (options.Otlp?.Enabled == true)
                    {
                        builder.AddOtlpExporter(exporter =>
                            exporter.Endpoint = new Uri(options.Otlp.Endpoint.Trim()));
                    }
                });

            return services;
        }

        internal static bool IsValid(ObservabilityOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ServiceName)
                || options.ServiceName.Trim().Length > 100
                || options.TraceSamplingRatio is <= 0 or > 1)
            {
                return false;
            }

            if (options.Otlp is null)
                return false;

            if (!options.Otlp.Enabled)
                return true;

            return Uri.TryCreate(options.Otlp.Endpoint?.Trim(), UriKind.Absolute, out var endpoint)
                && endpoint.Scheme is "http" or "https"
                && string.IsNullOrEmpty(endpoint.UserInfo)
                && string.IsNullOrEmpty(endpoint.Query)
                && string.IsNullOrEmpty(endpoint.Fragment);
        }

        private static void RedactClientUrl(
            Activity activity,
            HttpRequestMessage request)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } uri)
                return;

            var sanitizedUri = new UriBuilder(
                uri.Scheme,
                uri.Host,
                uri.IsDefaultPort ? -1 : uri.Port,
                uri.AbsolutePath).Uri;
            var sanitizedUrl = sanitizedUri.GetLeftPart(UriPartial.Path);
            activity.SetTag("url.full", sanitizedUrl);
            activity.SetTag("http.url", sanitizedUrl);
        }
    }
}
