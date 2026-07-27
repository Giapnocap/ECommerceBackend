using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class AuditRepository : IAuditRepository
    {
        private readonly AppDbContext _context;

        public AuditRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PageSlice<AuditEventResponse>> QueryAsync(
            Guid? actorUserId,
            string? action,
            string? entityType,
            DateTime? from,
            DateTime? to,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var events = _context.AuditEvents.AsNoTracking().AsQueryable();
            if (actorUserId.HasValue)
                events = events.Where(item => item.ActorUserId == actorUserId);
            if (!string.IsNullOrWhiteSpace(action))
                events = events.Where(item => item.Action == action);
            if (!string.IsNullOrWhiteSpace(entityType))
                events = events.Where(item => item.EntityType == entityType);
            if (from.HasValue)
                events = events.Where(item => item.CreatedAt >= from.Value);
            if (to.HasValue)
                events = events.Where(item => item.CreatedAt < to.Value);

            var totalCount = await events.CountAsync(cancellationToken);
            var items = await events
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .Skip(skip)
                .Take(take)
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

            return new PageSlice<AuditEventResponse>(items, totalCount);
        }
    }
}
