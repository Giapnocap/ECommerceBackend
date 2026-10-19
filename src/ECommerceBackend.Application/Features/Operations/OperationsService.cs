using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class OperationsService : IOperationsService
    {
        private readonly AuditQueryUseCase _auditQuery;

        public OperationsService(AuditQueryUseCase auditQuery)
        {
            _auditQuery = auditQuery;
        }

        public Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
            => _auditQuery.ExecuteAsync(query, cancellationToken);

        public Task<AuditEventResponse> GetAuditEventAsync(
            Guid id,
            CancellationToken cancellationToken = default)
            => _auditQuery.GetByIdAsync(id, cancellationToken);
    }
}
