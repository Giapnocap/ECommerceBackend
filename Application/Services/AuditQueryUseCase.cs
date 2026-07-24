using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class AuditQueryUseCase
    {
        private readonly IAppDbContext _context;

        public AuditQueryUseCase(IAppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<AuditEventResponse>> ExecuteAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                query.Page,
                query.PageSize,
                defaultSize: 20);
            var events = _context.AuditEvents.AsNoTracking().AsQueryable();
            if (query.ActorUserId.HasValue)
            {
                events = events.Where(
                    item => item.ActorUserId == query.ActorUserId);
            }
            if (!string.IsNullOrWhiteSpace(query.Action))
                events = events.Where(item => item.Action == query.Action.Trim());
            if (!string.IsNullOrWhiteSpace(query.EntityType))
            {
                events = events.Where(
                    item => item.EntityType == query.EntityType.Trim());
            }
            if (query.From.HasValue)
                events = events.Where(item => item.CreatedAt >= query.From.Value);
            if (query.To.HasValue)
                events = events.Where(item => item.CreatedAt < query.To.Value);

            var totalCount = await events.CountAsync(cancellationToken);
            var items = await events
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(item => new AuditEventResponse
                {
                    Id = item.Id,
                    ActorUserId = item.ActorUserId,
                    Action = item.Action,
                    EntityType = item.EntityType,
                    EntityId = item.EntityId,
                    CorrelationId = item.CorrelationId,
                    IpAddress = item.IpAddress,
                    MetadataJson = item.MetadataJson,
                    CreatedAt = item.CreatedAt
                })
                .ToListAsync(cancellationToken);
            return PagedResult<AuditEventResponse>.Create(
                items,
                totalCount,
                paging.Page,
                paging.Size);
        }
    }
}
