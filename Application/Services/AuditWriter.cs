using System.Diagnostics.Metrics;
using System.Security.Claims;
using System.Text.Json;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuditWriter : IAuditWriter
    {
        private const int MaxMetadataLength = 4000;
        private static readonly Meter Meter = new("ECommerceBackend.Operations");
        private static readonly Counter<long> AuditCounter = Meter.CreateCounter<long>("audit.events.enqueued");

        private readonly IAuditRepository _auditRepository;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly TimeProvider _timeProvider;

        public AuditWriter(
            IAuditRepository auditRepository,
            IHttpContextAccessor httpContextAccessor,
            TimeProvider timeProvider)
        {
            _auditRepository = auditRepository;
            _httpContextAccessor = httpContextAccessor;
            _timeProvider = timeProvider;
        }

        public void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            var resolvedActor = actorUserId ?? TryGetActorUserId(httpContext?.User);
            var metadataJson = metadata is { Count: > 0 }
                ? JsonSerializer.Serialize(metadata)
                : null;
            if (metadataJson?.Length > MaxMetadataLength)
                metadataJson = JsonSerializer.Serialize(new { truncated = true });

            _auditRepository.Add(new AuditEvent
            {
                Id = Guid.NewGuid(),
                ActorUserId = resolvedActor,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                CorrelationId = httpContext?.TraceIdentifier ?? "background",
                IpAddress = httpContext?.Connection.RemoteIpAddress?.ToString(),
                MetadataJson = metadataJson,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            });
            AuditCounter.Add(1, new KeyValuePair<string, object?>("action", action));
        }

        private static Guid? TryGetActorUserId(ClaimsPrincipal? principal)
            => Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                ? userId
                : null;
    }
}
