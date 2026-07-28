using System.Diagnostics;
using Serilog.Context;

namespace ECommerceBackend.API.Middlewares
{
    public sealed class CorrelationIdMiddleware
    {
        public const string HeaderName = "X-Correlation-ID";
        private const int MaxCorrelationIdLength = 128;
        private readonly RequestDelegate _next;

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var correlationId = ResolveCorrelationId(context);
            var traceId = Activity.Current?.TraceId.ToString() ?? correlationId;
            var spanId = Activity.Current?.SpanId.ToString() ?? string.Empty;
            context.TraceIdentifier = correlationId;
            context.Response.Headers[HeaderName] = correlationId;

            using (LogContext.PushProperty("CorrelationId", correlationId))
            using (LogContext.PushProperty("TraceId", traceId))
            using (LogContext.PushProperty("SpanId", spanId))
            {
                await _next(context);
            }
        }

        private static string ResolveCorrelationId(HttpContext context)
        {
            var requestedId = context.Request.Headers[HeaderName].ToString();
            if (IsValid(requestedId))
                return requestedId;

            var activityTraceId = Activity.Current?.TraceId.ToString();
            return string.IsNullOrWhiteSpace(activityTraceId)
                ? Guid.NewGuid().ToString("N")
                : activityTraceId;
        }

        private static bool IsValid(string value)
            => value.Length is > 0 and <= MaxCorrelationIdLength
                && value.All(character => char.IsAsciiLetterOrDigit(character)
                    || character is '-' or '_' or '.');
    }
}
