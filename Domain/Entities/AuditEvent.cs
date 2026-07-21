namespace ECommerceBackend.Domain.Entities
{
    public sealed class AuditEvent
    {
        public Guid Id { get; set; }
        public Guid? ActorUserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string? EntityId { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public string? MetadataJson { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
