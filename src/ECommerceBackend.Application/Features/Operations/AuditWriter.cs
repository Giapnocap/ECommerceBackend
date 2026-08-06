using System.Diagnostics.Metrics;
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
        private readonly IRequestContext _requestContext;
        private readonly TimeProvider _timeProvider;

        public AuditWriter(
            IAuditRepository auditRepository,
            IRequestContext requestContext,
            TimeProvider timeProvider)
        {
            _auditRepository = auditRepository;
            _requestContext = requestContext;
            _timeProvider = timeProvider;
        }

        public void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
            var resolvedActor = actorUserId ?? _requestContext.ActorUserId;
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
                CorrelationId = _requestContext.CorrelationId,
                IpAddress = _requestContext.IpAddress,
                MetadataJson = metadataJson,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
            });
            AuditCounter.Add(1, new KeyValuePair<string, object?>("action", action));
        }
    }
}
