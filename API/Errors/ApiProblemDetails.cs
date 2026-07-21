using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

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
                Title = ReasonPhrases.GetReasonPhrase(statusCode),
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
                Problem.Detail ?? Problem.Title ?? "Request failed.",
                Problem.Extensions["errors"] as IReadOnlyDictionary<string, string[]>,
                Problem.Extensions["details"]?.ToString() ?? string.Empty,
                context.HttpContext.RequestAborted);
    }
}
