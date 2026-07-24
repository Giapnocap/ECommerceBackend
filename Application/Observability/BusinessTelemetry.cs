using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ECommerceBackend.Application.Observability
{
    internal static class BusinessTelemetry
    {
        public const string ActivitySourceName = "ECommerceBackend.Business";
        public const string MeterName = "ECommerceBackend.Business";

        private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
        private static readonly Meter Meter = new(MeterName);
        private static readonly Counter<long> OperationCounter =
            Meter.CreateCounter<long>("commerce.operations");
        private static readonly Histogram<double> OperationDuration =
            Meter.CreateHistogram<double>("commerce.operation.duration", "ms");

        public static BusinessOperation Start(
            string operationName,
            CancellationToken cancellationToken = default,
            params KeyValuePair<string, object?>[] tags)
            => new(
                operationName,
                cancellationToken,
                tags,
                ActivitySource,
                OperationCounter,
                OperationDuration);
    }

    internal sealed class BusinessOperation : IDisposable
    {
        private readonly string _operationName;
        private readonly CancellationToken _cancellationToken;
        private readonly IReadOnlyList<KeyValuePair<string, object?>> _tags;
        private readonly Counter<long> _counter;
        private readonly Histogram<double> _duration;
        private readonly Activity? _activity;
        private readonly long _startedAt;
        private bool _completed;
        private bool _disposed;

        internal BusinessOperation(
            string operationName,
            CancellationToken cancellationToken,
            IReadOnlyList<KeyValuePair<string, object?>> tags,
            ActivitySource activitySource,
            Counter<long> counter,
            Histogram<double> duration)
        {
            _operationName = operationName;
            _cancellationToken = cancellationToken;
            _tags = tags;
            _counter = counter;
            _duration = duration;
            _startedAt = Stopwatch.GetTimestamp();
            _activity = activitySource.StartActivity(operationName, ActivityKind.Internal);
            _activity?.SetTag("operation.name", operationName);

            foreach (var tag in tags)
                _activity?.SetTag(tag.Key, tag.Value);
        }

        public void SetTag(string name, object? value)
            => _activity?.SetTag(name, value);

        public void Complete()
        {
            _completed = true;
            _activity?.SetStatus(ActivityStatusCode.Ok);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            var outcome = _completed
                ? "success"
                : _cancellationToken.IsCancellationRequested
                    ? "cancelled"
                    : "error";
            var metricTags = new TagList
            {
                { "operation.name", _operationName },
                { "operation.outcome", outcome }
            };
            foreach (var tag in _tags)
                metricTags.Add(tag.Key, tag.Value);

            if (!_completed && outcome == "error")
                _activity?.SetStatus(ActivityStatusCode.Error, "Business operation failed.");

            _activity?.SetTag("operation.outcome", outcome);
            _counter.Add(1, metricTags);
            _duration.Record(
                Stopwatch.GetElapsedTime(_startedAt).TotalMilliseconds,
                metricTags);
            _activity?.Dispose();
        }
    }
}
