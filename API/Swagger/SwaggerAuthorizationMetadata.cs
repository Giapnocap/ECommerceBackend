using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace ECommerceBackend.API.Swagger
{
    public static class SwaggerAuthorizationMetadata
    {
        public static bool RequiresAuthorization(MethodInfo methodInfo)
        {
            var controllerType = methodInfo.DeclaringType
                ?? throw new ArgumentException("Action must have a declaring controller type.", nameof(methodInfo));

            var allowsAnonymous = controllerType
                    .GetCustomAttributes(true)
                    .OfType<AllowAnonymousAttribute>()
                    .Any()
                || methodInfo
                    .GetCustomAttributes(true)
                    .OfType<AllowAnonymousAttribute>()
                    .Any();

            // AddECommerceAuthorization applies an authenticated fallback policy.
            return !allowsAnonymous;
        }
    }
}
