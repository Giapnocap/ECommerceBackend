using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IAuditRepository
    {
        Task<PageSlice<AuditEventResponse>> QueryAsync(
            Guid? actorUserId,
            string? action,
            string? entityType,
            DateTime? from,
            DateTime? to,
            int skip,
            int take,
            CancellationToken cancellationToken = default);
    }
}
