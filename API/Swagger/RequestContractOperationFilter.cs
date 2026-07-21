using ECommerceBackend.API.Controllers;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ECommerceBackend.API.Swagger
{
    public sealed class RequestContractOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (IsAction<OrderController>(context, nameof(OrderController.PlaceOrder)))
            {
                ConfigureRequiredHeader(
                    operation,
                    "Idempotency-Key",
                    "Unique key for safely retrying this checkout request.");
            }

            if (!IsAction<PaymentController>(context, nameof(PaymentController.HandleWebhook)))
                return;

            ConfigureParameter(
                operation,
                "providerCode",
                ParameterLocation.Path,
                "Code of the configured payment provider.",
                required: true);
            ConfigureRequiredHeader(
                operation,
                "X-Payment-Event-Id",
                "Provider event identifier used for webhook idempotency.");
            ConfigureRequiredHeader(
                operation,
                "X-Payment-Signature",
                "Signature calculated by the configured payment provider.");

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "Provider-specific JSON payload. The exact UTF-8 request bytes are used for signature verification.",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            AdditionalPropertiesAllowed = true
                        }
                    }
                }
            };
        }

        private static void ConfigureRequiredHeader(
            OpenApiOperation operation,
            string name,
            string description)
            => ConfigureParameter(
                operation,
                name,
                ParameterLocation.Header,
                description,
                required: true);

        private static void ConfigureParameter(
            OpenApiOperation operation,
            string name,
            ParameterLocation location,
            string description,
            bool required)
        {
            operation.Parameters ??= new List<OpenApiParameter>();
            var parameter = operation.Parameters.FirstOrDefault(candidate =>
                candidate.In == location
                && string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

            if (parameter == null)
            {
                parameter = new OpenApiParameter
                {
                    Name = name,
                    In = location,
                    Schema = new OpenApiSchema { Type = "string" }
                };
                operation.Parameters.Add(parameter);
            }

            parameter.Required = required;
            parameter.Description = description;
        }

        private static bool IsAction<TController>(
            OperationFilterContext context,
            string actionName)
            => context.MethodInfo.DeclaringType == typeof(TController)
                && string.Equals(context.MethodInfo.Name, actionName, StringComparison.Ordinal);
    }
}
