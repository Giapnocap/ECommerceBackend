using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Swagger
{
    public sealed class ApiErrorResponse : ProblemDetails
    {
        public string Message { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;

        public string TraceId { get; set; } = string.Empty;

        public string Details { get; set; } = string.Empty;

        public IDictionary<string, string[]>? Errors { get; set; }
    }
}
