using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ECommerceBackend.API.Swagger
{
    public sealed class CanonicalApiVersionDocumentFilter : IDocumentFilter
    {
        private const string ApiPrefix = "/api/";
        private const string VersionOnePrefix = "/api/v1/";

        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            var legacyPaths = swaggerDoc.Paths.Keys
                .Where(path => path.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith(
                        VersionOnePrefix,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var path in legacyPaths)
                swaggerDoc.Paths.Remove(path);
        }
    }
}
