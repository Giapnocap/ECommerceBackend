using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Health
{
    public static class HealthCheckResponseWriter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static Task WritePublicAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";
            var response = new { status = report.Status.ToString() };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
        }

        public static Task WriteDetailedAsync(HttpContext context, HealthReport report)
        {
            context.Response.ContentType = "application/json";

            var response = new
            {
                status = report.Status.ToString(),
                totalDurationMs = report.TotalDuration.TotalMilliseconds,
                checks = report.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    data = entry.Value.Data
                })
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
        }
    }
}
