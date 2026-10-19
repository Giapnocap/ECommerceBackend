using ECommerceBackend.API.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace ECommerceBackend.API.Extensions
{
    public static class EndpointRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapECommerceHealthChecks(
            this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("live"),
                ResponseWriter = HealthCheckResponseWriter.WritePublicAsync
            }).AllowAnonymous();

            endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready"),
                ResponseWriter = HealthCheckResponseWriter.WritePublicAsync
            }).AllowAnonymous();

            endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = HealthCheckResponseWriter.WritePublicAsync
            }).AllowAnonymous();

            return endpoints;
        }
    }
}
