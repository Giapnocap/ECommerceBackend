using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ECommerceBackend.API.Errors
{
    public sealed class ECommerceProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
        {
            var status = statusCode ?? StatusCodes.Status500InternalServerError;
            var response = ResolveResponse(status);
            return ApiProblemDetails.Create(
                httpContext,
                status,
                response.Code,
                response.Message);
        }

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelStateDictionary,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null)
        {
            ArgumentNullException.ThrowIfNull(modelStateDictionary);

            var errors = modelStateDictionary
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value!.Errors
                        .Select(_ => "Giá trị không hợp lệ hoặc không đúng định dạng.")
                        .ToArray());
            var source = ApiProblemDetails.Create(
                httpContext,
                statusCode ?? StatusCodes.Status400BadRequest,
                "validation_error",
                "Dữ liệu gửi lên không hợp lệ.",
                errors);
            var problem = new ValidationProblemDetails(errors)
            {
                Type = source.Type,
                Title = source.Title,
                Status = source.Status,
                Detail = source.Detail,
                Instance = source.Instance
            };
            foreach (var extension in source.Extensions)
                problem.Extensions[extension.Key] = extension.Value;

            return problem;
        }

        private static (string Code, string Message) ResolveResponse(int statusCode)
            => statusCode switch
            {
                StatusCodes.Status400BadRequest => (
                    "bad_request",
                    "Yêu cầu không hợp lệ."),
                StatusCodes.Status401Unauthorized => (
                    "unauthorized",
                    "Bạn cần đăng nhập để thực hiện yêu cầu này."),
                StatusCodes.Status403Forbidden => (
                    "forbidden",
                    "Bạn không có quyền thực hiện thao tác này."),
                StatusCodes.Status404NotFound => (
                    "endpoint_not_found",
                    "Không tìm thấy endpoint được yêu cầu."),
                StatusCodes.Status405MethodNotAllowed => (
                    "method_not_allowed",
                    "Phương thức HTTP không được hỗ trợ cho endpoint này."),
                StatusCodes.Status406NotAcceptable => (
                    "not_acceptable",
                    "Máy chủ không hỗ trợ định dạng phản hồi được yêu cầu."),
                StatusCodes.Status409Conflict => (
                    "conflict",
                    "Yêu cầu xung đột với trạng thái dữ liệu hiện tại."),
                StatusCodes.Status413PayloadTooLarge => (
                    "request_too_large",
                    "Dữ liệu gửi lên vượt quá giới hạn cho phép."),
                StatusCodes.Status415UnsupportedMediaType => (
                    "unsupported_media_type",
                    "Định dạng dữ liệu gửi lên không được hỗ trợ."),
                StatusCodes.Status429TooManyRequests => (
                    "rate_limit_exceeded",
                    "Quá nhiều yêu cầu. Vui lòng thử lại sau."),
                _ => (
                    "internal_server_error",
                    "Lỗi hệ thống. Vui lòng thử lại sau.")
            };
    }
}
