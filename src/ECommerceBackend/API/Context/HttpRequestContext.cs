using System.Security.Claims;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.API.Context
{
    internal sealed class HttpRequestContext : IRequestContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpRequestContext(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? ActorUserId
            => Guid.TryParse(
                _httpContextAccessor.HttpContext?.User.FindFirstValue(
                    ClaimTypes.NameIdentifier),
                out var userId)
                ? userId
                : null;

        public string CorrelationId
            => _httpContextAccessor.HttpContext?.TraceIdentifier ?? "background";

        public string? IpAddress
            => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    }
}
