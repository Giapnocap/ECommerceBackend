using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
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

        public void Add(AuditEvent auditEvent)
            => _context.AuditEvents.Add(auditEvent);

        public async Task<PageSlice<AuditEventResponse>> QueryAsync(
            Guid? actorUserId,
            string? action,
            string? entityType,
            string? entityId,
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
            if (!string.IsNullOrWhiteSpace(entityId))
                events = events.Where(item => item.EntityId == entityId);
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

            RedactMetadata(items);
            await EnrichAuditNamesAsync(items, cancellationToken);

            return new PageSlice<AuditEventResponse>(items, totalCount);
        }

        public async Task<AuditEventResponse?> GetByIdAsync(
            Guid id,
            CancellationToken cancellationToken = default)
        {
            var auditEvent = await _context.AuditEvents
                .AsNoTracking()
                .Where(item => item.Id == id)
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
                .SingleOrDefaultAsync(cancellationToken);
            if (auditEvent != null)
            {
                auditEvent.MetadataJson = AuditMetadataRedactor.RedactJson(auditEvent.MetadataJson);
                await EnrichAuditNamesAsync(new[] { auditEvent }, cancellationToken);
            }

            return auditEvent;
        }

        private static void RedactMetadata(IEnumerable<AuditEventResponse> items)
        {
            foreach (var item in items)
                item.MetadataJson = AuditMetadataRedactor.RedactJson(item.MetadataJson);
        }

        private async Task EnrichAuditNamesAsync(IReadOnlyList<AuditEventResponse> items, CancellationToken cancellationToken)
        {
            if (items.Count == 0) return;

            var actorIds = items
                .Where(x => x.ActorUserId.HasValue)
                .Select(x => x.ActorUserId!.Value)
                .Distinct()
                .ToList();

            Dictionary<Guid, string> actorMap = new();
            if (actorIds.Count > 0)
            {
                var users = await _context.Users
                    .AsNoTracking()
                    .Where(u => actorIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName, u.FullName })
                    .ToListAsync(cancellationToken);

                actorMap = users.ToDictionary(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.FullName) ? $"{u.FullName} ({u.UserName})" : u.UserName);
            }

            var productIds = items
                .Where(x => x.EntityType.Equals("Product", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId!))
                .Distinct()
                .ToList();
            Dictionary<Guid, string> productMap = new();
            if (productIds.Count > 0)
            {
                productMap = await _context.Products
                    .AsNoTracking()
                    .Where(p => productIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.Name })
                    .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
            }

            var categoryIds = items
                .Where(x => x.EntityType.Equals("Category", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId!))
                .Distinct()
                .ToList();
            Dictionary<Guid, string> categoryMap = new();
            if (categoryIds.Count > 0)
            {
                categoryMap = await _context.Categories
                    .AsNoTracking()
                    .Where(c => categoryIds.Contains(c.Id))
                    .Select(c => new { c.Id, c.Name })
                    .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
            }

            var orderIds = items
                .Where(x => x.EntityType.Equals("Order", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId!))
                .Distinct()
                .ToList();
            Dictionary<Guid, string> orderMap = new();
            if (orderIds.Count > 0)
            {
                var orders = await _context.Orders
                    .AsNoTracking()
                    .Where(o => orderIds.Contains(o.Id))
                    .Select(o => new { o.Id, o.OrderNumber })
                    .ToListAsync(cancellationToken);

                orderMap = orders.ToDictionary(
                    o => o.Id,
                    o => !string.IsNullOrWhiteSpace(o.OrderNumber) ? $"Đơn hàng {o.OrderNumber}" : $"Đơn hàng #{o.Id.ToString()[..8].ToUpper()}");
            }

            var userEntityIds = items
                .Where(x => x.EntityType.Equals("User", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(x.EntityId, out _))
                .Select(x => Guid.Parse(x.EntityId!))
                .Distinct()
                .ToList();
            Dictionary<Guid, string> userEntityMap = new();
            if (userEntityIds.Count > 0)
            {
                var targetUsers = await _context.Users
                    .AsNoTracking()
                    .Where(u => userEntityIds.Contains(u.Id))
                    .Select(u => new { u.Id, u.UserName, u.FullName })
                    .ToListAsync(cancellationToken);

                userEntityMap = targetUsers.ToDictionary(
                    u => u.Id,
                    u => !string.IsNullOrWhiteSpace(u.FullName) ? $"{u.FullName} ({u.UserName})" : u.UserName);
            }

            foreach (var item in items)
            {
                if (item.ActorUserId.HasValue && actorMap.TryGetValue(item.ActorUserId.Value, out var actorName))
                {
                    item.ActorName = actorName;
                }
                else if (!item.ActorUserId.HasValue)
                {
                    item.ActorName = "Hệ thống";
                }

                if (!string.IsNullOrWhiteSpace(item.EntityId) && Guid.TryParse(item.EntityId, out var entityGuid))
                {
                    if (item.EntityType.Equals("Product", StringComparison.OrdinalIgnoreCase) && productMap.TryGetValue(entityGuid, out var prodName))
                    {
                        item.EntityName = prodName;
                    }
                    else if (item.EntityType.Equals("Category", StringComparison.OrdinalIgnoreCase) && categoryMap.TryGetValue(entityGuid, out var catName))
                    {
                        item.EntityName = catName;
                    }
                    else if (item.EntityType.Equals("Order", StringComparison.OrdinalIgnoreCase) && orderMap.TryGetValue(entityGuid, out var ordName))
                    {
                        item.EntityName = ordName;
                    }
                    else if (item.EntityType.Equals("User", StringComparison.OrdinalIgnoreCase) && userEntityMap.TryGetValue(entityGuid, out var usrName))
                    {
                        item.EntityName = usrName;
                    }
                }
            }
        }
    }
}
