using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using ECommerceBackend.API.Extensions;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Infrastructure.Observability;
using ECommerceBackend.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ECommerceBackend.Tests;

public sealed class ObservabilityTests
{
    private const string BusinessSourceName = "ECommerceBackend.Business";

    [Fact]
    public void Registration_WithValidConfiguration_RegistersProvidersAndSqlTelemetry()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["Observability:ServiceName"] = "ECommerceBackend.Tests",
            ["Observability:TraceSamplingRatio"] = "0.5",
            ["Observability:Otlp:Enabled"] = "false",
            ["Observability:Otlp:Endpoint"] = "http://localhost:4317"
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddECommerceObservability(
            configuration,
            new TestWebHostEnvironment(AppContext.BaseDirectory));

        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        Assert.Equal("ECommerceBackend.Tests", options.ServiceName);
        Assert.Equal(0.5, options.TraceSamplingRatio);
        Assert.NotNull(provider.GetRequiredService<DatabaseTelemetryInterceptor>());
        Assert.NotNull(provider.GetRequiredService<TracerProvider>());
        Assert.NotNull(provider.GetRequiredService<MeterProvider>());
    }

    [Theory]
    [InlineData("0", "false", "http://localhost:4317")]
    [InlineData("1.1", "false", "http://localhost:4317")]
    [InlineData("1", "true", "ftp://collector.example.com")]
    [InlineData("1", "true", "http://user:password@collector.example.com")]
    public void Registration_WithInvalidConfiguration_FailsOptionsValidation(
        string samplingRatio,
        string exporterEnabled,
        string endpoint)
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["Observability:Enabled"] = "true",
            ["Observability:ServiceName"] = "ECommerceBackend.Tests",
            ["Observability:TraceSamplingRatio"] = samplingRatio,
            ["Observability:Otlp:Enabled"] = exporterEnabled,
            ["Observability:Otlp:Endpoint"] = endpoint
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddECommerceObservability(
            configuration,
            new TestWebHostEnvironment(AppContext.BaseDirectory));

        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ObservabilityOptions>>().Value);
    }

    [Fact]
    public async Task RegisterTelemetry_DoesNotRecordCredentialsOrIssuedTokens()
    {
        var activities = new ConcurrentQueue<Activity>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == BusinessSourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Enqueue
        };
        ActivitySource.AddActivityListener(activityListener);

        var measurements = new ConcurrentQueue<TelemetryMeasurement>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == BusinessSourceName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            measurements.Enqueue(new TelemetryMeasurement(
                instrument.Name,
                CopyTags(tags))));
        meterListener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            measurements.Enqueue(new TelemetryMeasurement(
                instrument.Name,
                CopyTags(tags))));
        meterListener.Start();

        const string userName = "observability_secret_user";
        const string email = "observability-secret@example.com";
        const string password = "SensitivePassword@123";
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateAuthService(context);

        using var parent = new Activity("observability-test").Start();
        var response = await service.RegisterAsync(new RegisterRequest
        {
            UserName = userName,
            Email = email,
            Password = password,
            FullName = "Observability Test"
        });
        parent.Stop();

        var activity = Assert.Single(activities, candidate =>
            candidate.OperationName == "auth.register"
            && candidate.ParentSpanId == parent.SpanId);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal("success", activity.GetTagItem("operation.outcome"));

        var recordedText = string.Join(
            '|',
            activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")
                .Concat(measurements.SelectMany(measurement =>
                    measurement.Tags.Select(tag => $"{tag.Key}={tag.Value}"))));

        Assert.DoesNotContain(userName, recordedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(email, recordedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, recordedText, StringComparison.Ordinal);
        Assert.DoesNotContain(response.AccessToken, recordedText, StringComparison.Ordinal);
        Assert.DoesNotContain(response.RefreshToken, recordedText, StringComparison.Ordinal);
        Assert.Contains(measurements, measurement =>
            measurement.Name == "commerce.operations"
            && measurement.Tags.Any(tag =>
                tag.Key == "operation.name"
                && tag.Value == "auth.register")
            && measurement.Tags.Any(tag =>
                tag.Key == "operation.outcome"
                && tag.Value == "success"));
    }

    private static IConfiguration CreateConfiguration(
        IDictionary<string, string?> values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IReadOnlyList<KeyValuePair<string, string?>> CopyTags(
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var snapshot = new List<KeyValuePair<string, string?>>(tags.Length);
        foreach (var tag in tags)
        {
            snapshot.Add(new KeyValuePair<string, string?>(
                tag.Key,
                tag.Value?.ToString()));
        }

        return snapshot;
    }

    private sealed record TelemetryMeasurement(
        string Name,
        IReadOnlyList<KeyValuePair<string, string?>> Tags);
}
