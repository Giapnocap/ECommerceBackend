using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Errors
{
    public static class ApiProblemDetails
    {
        public const string ContentType = "application/problem+json";

        public static ProblemDetails Create(
            HttpContext context,
            int statusCode,
            string code,
            string message,
            IReadOnlyDictionary<string, string[]>? errors = null,
            string details = "")
        {
            var problem = new ProblemDetails
            {
                Type = $"https://httpstatuses.com/{statusCode}",
                Title = GetTitle(statusCode),
                Status = statusCode,
                Detail = message,
                Instance = context.Request.Path.HasValue
                    ? context.Request.Path.Value
                    : null
            };
            problem.Extensions["message"] = message;
            problem.Extensions["code"] = code;
            problem.Extensions["traceId"] = context.TraceIdentifier;
            problem.Extensions["details"] = details;
            problem.Extensions["errors"] = errors;
            return problem;
        }

        private static string GetTitle(int statusCode)
            => statusCode switch
            {
                StatusCodes.Status400BadRequest => "Yêu cầu không hợp lệ",
                StatusCodes.Status401Unauthorized => "Chưa xác thực",
                StatusCodes.Status403Forbidden => "Không có quyền truy cập",
                StatusCodes.Status404NotFound => "Không tìm thấy",
                StatusCodes.Status405MethodNotAllowed => "Phương thức không được hỗ trợ",
                StatusCodes.Status406NotAcceptable => "Định dạng phản hồi không được hỗ trợ",
                StatusCodes.Status409Conflict => "Xung đột dữ liệu",
                StatusCodes.Status413PayloadTooLarge => "Dữ liệu gửi lên quá lớn",
                StatusCodes.Status415UnsupportedMediaType => "Định dạng dữ liệu không được hỗ trợ",
                StatusCodes.Status429TooManyRequests => "Quá nhiều yêu cầu",
                StatusCodes.Status500InternalServerError => "Lỗi máy chủ",
                _ => "Lỗi xử lý yêu cầu"
            };

        public static Task WriteAsync(
            HttpContext context,
            int statusCode,
            string code,
            string message,
            IReadOnlyDictionary<string, string[]>? errors = null,
            string details = "",
            CancellationToken cancellationToken = default)
        {
            if (context.Response.HasStarted)
                return Task.CompletedTask;

            context.Response.StatusCode = statusCode;
            return context.Response.WriteAsJsonAsync(
                Create(context, statusCode, code, message, errors, details),
                options: null,
                contentType: ContentType,
                cancellationToken: cancellationToken);
        }
    }

    public sealed class ApiProblemDetailsResult : IActionResult
    {
        public ApiProblemDetailsResult(ProblemDetails problem)
        {
            Problem = problem;
        }

        public ProblemDetails Problem { get; }

        public Task ExecuteResultAsync(ActionContext context)
            => ApiProblemDetails.WriteAsync(
                context.HttpContext,
                Problem.Status ?? StatusCodes.Status500InternalServerError,
                Problem.Extensions["code"]?.ToString() ?? "internal_server_error",
                Problem.Detail ?? Problem.Title ?? "Yêu cầu xử lý thất bại.",
                Problem.Extensions["errors"] as IReadOnlyDictionary<string, string[]>,
                Problem.Extensions["details"]?.ToString() ?? string.Empty,
                context.HttpContext.RequestAborted);
    }
}
