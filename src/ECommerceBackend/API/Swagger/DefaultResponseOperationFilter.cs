using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ECommerceBackend.API.Swagger
{
    public class DefaultResponseOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            var errorSchema = context.SchemaGenerator.GenerateSchema(
                typeof(ApiErrorResponse),
                context.SchemaRepository);

            AddErrorResponse(
                operation,
                "400",
                "Yêu cầu không hợp lệ do dữ liệu đầu vào hoặc quy tắc nghiệp vụ.",
                errorSchema);
            AddErrorResponse(operation, "500", "Lỗi xử lý nội bộ của máy chủ.", errorSchema);

            if (SwaggerAuthorizationMetadata.RequiresAuthorization(context.MethodInfo)
                || IsPaymentWebhook(context))
            {
                AddErrorResponse(operation, "401", "Chưa xác thực - mã truy cập bị thiếu hoặc không hợp lệ.", errorSchema);
            }

            if (SwaggerAuthorizationMetadata.RequiresAuthorization(context.MethodInfo))
            {
                AddErrorResponse(
                    operation,
                    "403",
                    "Người dùng đã xác thực nhưng không có đủ quyền thực hiện thao tác.",
                    errorSchema);
            }

            if (CanReturnNotFound(context))
                AddErrorResponse(operation, "404", "Không tìm thấy tài nguyên được yêu cầu.", errorSchema);

            if (CanReturnConflict(context))
            {
                AddErrorResponse(
                    operation,
                    "409",
                    "Dữ liệu xung đột do tài nguyên đã được một thao tác khác thay đổi.",
                    errorSchema);
            }

            if (CanReturnPayloadTooLarge(context))
                AddErrorResponse(operation, "413", "Dữ liệu gửi lên vượt quá giới hạn cấu hình.", errorSchema);

            if (HasAttribute<EnableRateLimitingAttribute>(context))
                AddErrorResponse(operation, "429", "Quá nhiều yêu cầu - đã vượt giới hạn truy cập.", errorSchema);
        }

        private static void AddErrorResponse(
            OpenApiOperation operation,
            string statusCode,
            string description,
            OpenApiSchema schema)
        {
            if (operation.Responses.ContainsKey(statusCode))
                return;

            operation.Responses.Add(statusCode, new OpenApiResponse
            {
                Description = description,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/problem+json"] = new() { Schema = schema }
                }
            });
        }

        private static bool CanReturnNotFound(OperationFilterContext context)
            => context.ApiDescription.RelativePath?.Contains("{id}", StringComparison.OrdinalIgnoreCase) == true
                || context.MethodInfo.Name.Contains("GetById", StringComparison.OrdinalIgnoreCase)
                || context.MethodInfo.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || context.MethodInfo.Name.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || IsPaymentWebhook(context);

        private static bool CanReturnConflict(OperationFilterContext context)
        {
            var controllerName = context.MethodInfo.DeclaringType?.Name;
            var methodName = context.MethodInfo.Name;

            return controllerName switch
            {
                "CartController" => !string.Equals(methodName, "GetMyCart", StringComparison.OrdinalIgnoreCase),
                "OrderController" => methodName is "PlaceOrder" or "UpdateStatus",
                "ProductController" => methodName is "Update" or "Delete" or "UploadImage" or "DeleteImage",
                "CategoryController" => methodName is "Create" or "Update" or "Delete",
                "UserController" => methodName is "AssignRole" or "ChangePassword",
                "AuthController" => methodName is "Register" or "Refresh" or "ResetPassword",
                "PaymentController" => methodName == "HandleWebhook",
                _ => false
            };
        }

        private static bool IsPaymentWebhook(OperationFilterContext context)
            => context.MethodInfo.DeclaringType?.Name == "PaymentController"
                && context.MethodInfo.Name == "HandleWebhook";

        private static bool CanReturnPayloadTooLarge(OperationFilterContext context)
            => IsPaymentWebhook(context)
                || context.ApiDescription.SupportedRequestFormats.Any(format =>
                    string.Equals(format.MediaType, "multipart/form-data", StringComparison.OrdinalIgnoreCase));

        private static bool HasAttribute<TAttribute>(OperationFilterContext context)
            where TAttribute : Attribute
            => context.MethodInfo.DeclaringType!
                .GetCustomAttributes(true)
                .OfType<TAttribute>()
                .Any()
                || context.MethodInfo
                    .GetCustomAttributes(true)
                    .OfType<TAttribute>()
                    .Any();
    }
}
