using ECommerceBackend.API.Errors;

namespace ECommerceBackend.API.Extensions
{
    public static class StatusCodePagesExtensions
    {
        public static IApplicationBuilder UseECommerceProblemDetailsContentType(
            this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    if (UsesFrameworkProblemDetails(context.Response.StatusCode)
                        && context.Response.ContentType?.StartsWith(
                            "application/json",
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        context.Response.ContentType = ApiProblemDetails.ContentType;
                    }

                    return Task.CompletedTask;
                });

                await next(context);
            });
        }

        private static bool UsesFrameworkProblemDetails(int statusCode)
            => statusCode is
                StatusCodes.Status400BadRequest
                or StatusCodes.Status404NotFound
                or StatusCodes.Status405MethodNotAllowed
                or StatusCodes.Status406NotAcceptable
                or StatusCodes.Status415UnsupportedMediaType;

        public static IApplicationBuilder UseECommerceUnmatchedEndpoint(
            this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                var endpoint = context.GetEndpoint();
                if (endpoint == null)
                {
                    await ApiProblemDetails.WriteAsync(
                        context,
                        StatusCodes.Status404NotFound,
                        "endpoint_not_found",
                        "Không tìm thấy endpoint được yêu cầu.",
                        cancellationToken: context.RequestAborted);
                    return;
                }

                if (string.Equals(
                    endpoint.DisplayName,
                    "405 HTTP Method Not Supported",
                    StringComparison.Ordinal))
                {
                    await ApiProblemDetails.WriteAsync(
                        context,
                        StatusCodes.Status405MethodNotAllowed,
                        "method_not_allowed",
                        "Phương thức HTTP không được hỗ trợ cho endpoint này.",
                        cancellationToken: context.RequestAborted);
                    return;
                }

                await next(context);
            });
        }

        public static IApplicationBuilder UseECommerceStatusCodePages(
            this IApplicationBuilder app)
        {
            return app.UseStatusCodePages(async context =>
            {
                var response = GetFallbackResponse(context.HttpContext.Response.StatusCode);
                if (response == null)
                    return;

                await ApiProblemDetails.WriteAsync(
                    context.HttpContext,
                    context.HttpContext.Response.StatusCode,
                    response.Value.Code,
                    response.Value.Message,
                    cancellationToken: context.HttpContext.RequestAborted);
            });
        }

        private static (string Code, string Message)? GetFallbackResponse(
            int statusCode)
            => statusCode switch
            {
                StatusCodes.Status400BadRequest => (
                    "bad_request",
                    "Yêu cầu không hợp lệ."),
                StatusCodes.Status404NotFound => (
                    "endpoint_not_found",
                    "Không tìm thấy endpoint được yêu cầu."),
                StatusCodes.Status405MethodNotAllowed => (
                    "method_not_allowed",
                    "Phương thức HTTP không được hỗ trợ cho endpoint này."),
                StatusCodes.Status406NotAcceptable => (
                    "not_acceptable",
                    "Máy chủ không hỗ trợ định dạng phản hồi được yêu cầu."),
                StatusCodes.Status415UnsupportedMediaType => (
                    "unsupported_media_type",
                    "Định dạng dữ liệu gửi lên không được hỗ trợ."),
                _ => null
            };
    }
}
