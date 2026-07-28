using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuditQueryUseCase
    {
        private readonly IAuditRepository _auditRepository;

        public AuditQueryUseCase(IAuditRepository auditRepository)
        {
            _auditRepository = auditRepository;
        }

        public async Task<PagedResult<AuditEventResponse>> ExecuteAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                query.Page,
                query.PageSize,
                defaultSize: 20);
            var result = await _auditRepository.QueryAsync(
                query.ActorUserId,
                query.Action?.Trim(),
                query.EntityType?.Trim(),
                query.From,
                query.To,
                Paging.GetSkipCount(paging),
                paging.Size,
                cancellationToken);
            return PagedResult<AuditEventResponse>.Create(
                result.Items,
                result.TotalCount,
                paging.Page,
                paging.Size);
        }
    }
}
