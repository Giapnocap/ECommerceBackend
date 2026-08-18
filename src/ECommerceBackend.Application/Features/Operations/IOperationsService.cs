using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IAuditWriter
    {
        void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null);
    }

    public interface IOperationsService
    {
        Task<PagedResult<DeadLetterResponse>> GetDeadLettersAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default);

        Task<RedriveOutboxResponse> RedriveDeadLetterAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default);

        Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default);

        Task<AuditEventResponse> GetAuditEventAsync(
            Guid id,
            CancellationToken cancellationToken = default);

        Task<DataRetentionResponse> RunDataRetentionAsync(
            DataRetentionRequest request,
            Guid? actorUserId,
            CancellationToken cancellationToken = default);
    }

    public interface IUploadReconciliationService
    {
        Task<UploadReconciliationResponse> ReconcileAsync(
            UploadReconciliationRequest request,
            Guid actorUserId,
            CancellationToken cancellationToken = default);
    }

    public sealed class NullAuditWriter : IAuditWriter
    {
        public static NullAuditWriter Instance { get; } = new();

        private NullAuditWriter() { }

        public void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
        {
        }
    }
}
