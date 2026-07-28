using ECommerceBackend.API.Errors;
using ECommerceBackend.Application.Exceptions;

namespace ECommerceBackend.API.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IHostEnvironment _env;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            IHostEnvironment env,
            ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _env = env;
            _logger = logger;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Request {TraceId} was aborted by the client.",
                    context.TraceIdentifier);
                if (!context.Response.HasStarted)
                    context.Response.StatusCode = 499;
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted)
                {
                    _logger.LogError(
                        ex,
                        "Unhandled exception after the response started for request {TraceId}.",
                        context.TraceIdentifier);
                    throw;
                }

                if (IsClientError(ex))
                {
                    _logger.LogWarning(
                        "Client error for request {TraceId}: {Message}",
                        context.TraceIdentifier,
                        ex.Message);
                }
                else
                {
                    _logger.LogError(
                        ex,
                        "Server error for request {TraceId}: {Message}",
                        context.TraceIdentifier,
                        ex.Message);
                }

                await HandleAsync(context, ex);
            }
        }

        private static bool IsClientError(Exception ex)
            => ex is ApiException or ArgumentException or BadHttpRequestException;

        private Task HandleAsync(HttpContext context, Exception ex)
        {
            var (status, code, message, errors) = ex switch
            {
                ApiException api => (api.StatusCode, api.Code, api.Message, api.Errors),
                BadHttpRequestException bad when bad.Message.Contains("Request body too large", StringComparison.OrdinalIgnoreCase)
                    => (413, "request_too_large", "Tệp vượt quá giới hạn cho phép.", (IReadOnlyDictionary<string, string[]>?)null),
                ArgumentException arg
                    => (400, "bad_request", arg.Message, (IReadOnlyDictionary<string, string[]>?)null),
                _ => (500, "internal_server_error", "Lỗi hệ thống. Vui lòng thử lại sau.", (IReadOnlyDictionary<string, string[]>?)null)
            };

            var details = _env.IsDevelopment() && ex is not ApiException
                ? ex.ToString()
                : string.Empty;
            return ApiProblemDetails.WriteAsync(
                context,
                status,
                code,
                message,
                errors,
                details,
                CancellationToken.None);
        }
    }
}
