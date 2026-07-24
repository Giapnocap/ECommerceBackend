namespace ECommerceBackend.Application.DTOs
{
    public sealed class DeadLetterQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class DeadLetterResponse
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public int Attempts { get; set; }
        public DateTime? LastAttemptAt { get; set; }
        public DateTime? DeadLetteredAt { get; set; }
        public string? LastError { get; set; }
    }

    public sealed class RedriveOutboxResponse
    {
        public Guid Id { get; set; }
        public bool ReDriven { get; set; }
        public DateTime NextAttemptAt { get; set; }
    }

    public sealed class AuditQueryParams
    {
        public Guid? ActorUserId { get; set; }
        public string? Action { get; set; }
        public string? EntityType { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class AuditEventResponse
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

    public sealed class UploadReconciliationRequest
    {
        public bool DeleteOrphans { get; set; }
        public int MaxDeletes { get; set; } = 50;
    }

    public sealed class UploadReconciliationResponse
    {
        public bool DryRun { get; set; }
        public int ScannedFileCount { get; set; }
        public int ReferencedFileCount { get; set; }
        public int MissingFileCount { get; set; }
        public int OrphanFileCount { get; set; }
        public int EligibleOrphanCount { get; set; }
        public int DeletedFileCount { get; set; }
        public IReadOnlyList<string> MissingFiles { get; set; } = [];
        public IReadOnlyList<string> OrphanFiles { get; set; } = [];
    }

    public sealed class DataRetentionRequest
    {
        public bool ApplyChanges { get; set; }
        public int MaxBatchSize { get; set; } = 100;
    }

    public sealed class DataRetentionResponse
    {
        public bool DryRun { get; set; }
        public bool ApplyChangesEnabled { get; set; }
        public int ProcessedOutboxCandidateCount { get; set; }
        public int ProcessedOutboxDeletedCount { get; set; }
        public int ExpiredRefreshTokenCandidateCount { get; set; }
        public int ExpiredRefreshTokenDeletedCount { get; set; }
        public int WebhookPayloadCandidateCount { get; set; }
        public int WebhookPayloadRedactedCount { get; set; }
        public DateTime ProcessedOutboxCutoff { get; set; }
        public DateTime ExpiredRefreshTokenCutoff { get; set; }
        public DateTime WebhookPayloadCutoff { get; set; }
    }
}
