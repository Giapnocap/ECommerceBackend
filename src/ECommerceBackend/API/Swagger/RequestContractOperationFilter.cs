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
                    "Khóa duy nhất để thử lại yêu cầu đặt hàng một cách an toàn.");
            }

            if (IsAction<ProductController>(context, nameof(ProductController.AdjustStock)))
            {
                ConfigureParameter(
                    operation,
                    "id",
                    ParameterLocation.Path,
                    "Mã sản phẩm cần điều chỉnh tồn kho.",
                    required: true);
                ConfigureRequiredHeader(
                    operation,
                    "If-Match",
                    "ETag mạnh nhận từ API sản phẩm, dùng để ngăn ghi đè tồn kho đã thay đổi.");
            }

            if (IsAction<PaymentController>(
                    context,
                    nameof(PaymentController.InitializeExternalPayment)))
            {
                ConfigureParameter(
                    operation,
                    "orderId",
                    ParameterLocation.Path,
                    "Ma don hang can khoi tao giao dich thanh toan.",
                    required: true);
            }

            if (IsAction<PaymentController>(
                    context,
                    nameof(PaymentController.HandleStripeWebhook)))
            {
                ConfigureRequiredHeader(
                    operation,
                    "Stripe-Signature",
                    "Chu ky webhook Stripe duoc xac minh tren raw request body.");
                ConfigureWebhookBody(operation);
                return;
            }

            if (!IsAction<PaymentController>(context, nameof(PaymentController.HandleWebhook)))
                return;

            ConfigureParameter(
                operation,
                "providerCode",
                ParameterLocation.Path,
                "Mã của cổng thanh toán đã cấu hình.",
                required: true);
            ConfigureRequiredHeader(
                operation,
                "X-Payment-Event-Id",
                "Mã sự kiện của cổng thanh toán dùng để chống xử lý trùng.");
            ConfigureRequiredHeader(
                operation,
                "X-Payment-Signature",
                "Chữ ký do cổng thanh toán đã cấu hình tạo ra.");

            ConfigureWebhookBody(operation);
        }

        private static void ConfigureWebhookBody(OpenApiOperation operation)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Description = "Nội dung JSON của cổng thanh toán. Chuỗi byte UTF-8 chính xác của yêu cầu được dùng để xác minh chữ ký.",
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
