using ECommerceBackend.API.Errors;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.API.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
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
            => ex switch
            {
                ApiException api => IsClientStatus(api.StatusCode),
                DomainRuleViolationException => true,
                BadHttpRequestException => true,
                _ => false
            };

        private Task HandleAsync(HttpContext context, Exception ex)
        {
            var (status, code, message, errors) = ex switch
            {
                ApiException api when IsClientStatus(api.StatusCode)
                    => (api.StatusCode, api.Code, api.Message, api.Errors),
                DomainRuleViolationException domain
                    => (StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message, (IReadOnlyDictionary<string, string[]>?)null),
                BadHttpRequestException bad when bad.Message.Contains("Request body too large", StringComparison.OrdinalIgnoreCase)
                    => (413, "request_too_large", "Tệp vượt quá giới hạn cho phép.", (IReadOnlyDictionary<string, string[]>?)null),
                BadHttpRequestException bad
                    => (NormalizeClientStatus(bad.StatusCode), "bad_request", "Yêu cầu HTTP không hợp lệ.", (IReadOnlyDictionary<string, string[]>?)null),
                _ => (500, "internal_server_error", "Lỗi hệ thống. Vui lòng thử lại sau.", (IReadOnlyDictionary<string, string[]>?)null)
            };

            return ApiProblemDetails.WriteAsync(
                context,
                status,
                code,
                message,
                errors,
                details: string.Empty,
                cancellationToken: CancellationToken.None);
        }

        private static int NormalizeClientStatus(int statusCode)
            => IsClientStatus(statusCode)
                ? statusCode
                : StatusCodes.Status400BadRequest;

        private static bool IsClientStatus(int statusCode)
            => statusCode is >= 400 and < 500;
    }
}
