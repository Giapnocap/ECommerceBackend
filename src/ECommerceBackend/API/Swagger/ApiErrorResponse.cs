using Microsoft.AspNetCore.Mvc;

namespace ECommerceBackend.API.Swagger
{
    /// <summary>Standard error payload returned by validation, business rules and exception middleware.</summary>
    public sealed class ApiErrorResponse : ProblemDetails
    {
        /// <summary>Human-readable error message.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Stable machine-readable error code.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>Request trace identifier for troubleshooting.</summary>
        public string TraceId { get; set; } = string.Empty;

        /// <summary>Development-only diagnostic details when available.</summary>
        public string Details { get; set; } = string.Empty;

        /// <summary>Field-level validation errors keyed by request property name.</summary>
        public IDictionary<string, string[]>? Errors { get; set; }
    }
}
