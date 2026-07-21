namespace ECommerceBackend.Domain.Entities
{
    public class OutboxMessage
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
        public int Attempts { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? DeadLetteredAt { get; set; }
        public Guid? LockId { get; set; }
        public DateTime? LockedAt { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public string? LastError { get; set; }
    }
}
