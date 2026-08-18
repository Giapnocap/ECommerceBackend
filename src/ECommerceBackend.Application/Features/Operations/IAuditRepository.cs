using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IAuditRepository
    {
        void Add(AuditEvent auditEvent);

        Task<PageSlice<AuditEventResponse>> QueryAsync(
            Guid? actorUserId,
            string? action,
            string? entityType,
            string? entityId,
            DateTime? from,
            DateTime? to,
            int skip,
            int take,
            CancellationToken cancellationToken = default);

        Task<AuditEventResponse?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default);
    }
}
