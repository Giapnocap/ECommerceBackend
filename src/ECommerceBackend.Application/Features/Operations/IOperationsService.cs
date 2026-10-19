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
        Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default);

        Task<AuditEventResponse> GetAuditEventAsync(
            Guid id,
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
