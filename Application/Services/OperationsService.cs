using System.Data;
using System.Diagnostics.Metrics;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class OperationsService : IOperationsService
    {
        private static readonly Meter Meter = new("ECommerceBackend.Operations");
        private static readonly Counter<long> RedriveCounter = Meter.CreateCounter<long>("outbox.dead_letters.redriven");

        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IAuditWriter _audit;
        private readonly TimeProvider _timeProvider;

        public OperationsService(
            IAppDbContext context,
            IDataConsistencyService consistency,
            IAuditWriter audit,
            TimeProvider timeProvider)
        {
            _context = context;
            _consistency = consistency;
            _audit = audit;
            _timeProvider = timeProvider;
        }

        public async Task<PagedResult<DeadLetterResponse>> GetDeadLettersAsync(
            DeadLetterQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var messages = _context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.DeadLetteredAt != null);
            var totalCount = await messages.CountAsync(cancellationToken);
            var items = await messages
                .OrderByDescending(message => message.DeadLetteredAt)
                .ThenBy(message => message.Id)
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .Select(message => new DeadLetterResponse
                {
                    Id = message.Id,
                    Type = message.Type,
                    OccurredAt = message.OccurredAt,
                    Attempts = message.Attempts,
                    LastAttemptAt = message.LastAttemptAt,
                    DeadLetteredAt = message.DeadLetteredAt,
                    LastError = message.LastError
                })
                .ToListAsync(cancellationToken);

            return PagedResult<DeadLetterResponse>.Create(items, totalCount, paging.Page, paging.Size);
        }

        public async Task<RedriveOutboxResponse> RedriveDeadLetterAsync(
            Guid messageId,
            Guid actorUserId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            var completed = false;

            try
            {
                var message = await _consistency.LockOutboxMessageAsync(messageId, cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy outbox message.");

                if (message.ProcessedAt.HasValue)
                    throw new ConflictException("Không thể re-drive một outbox message đã xử lý thành công.");

                if (!message.DeadLetteredAt.HasValue)
                {
                    await transaction.CommitAsync(cancellationToken);
                    completed = true;
                    return new RedriveOutboxResponse
                    {
                        Id = message.Id,
                        ReDriven = false,
                        NextAttemptAt = message.NextAttemptAt
                    };
                }

                var previousAttempts = message.Attempts;
                var previousDeadLetteredAt = message.DeadLetteredAt;
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                message.Attempts = 0;
                message.NextAttemptAt = now;
                message.DeadLetteredAt = null;
                message.LockId = null;
                message.LockedAt = null;
                message.LastError = null;

                _audit.Write(
                    "outbox.dead_letter.redrive",
                    "OutboxMessage",
                    message.Id.ToString(),
                    actorUserId,
                    new Dictionary<string, object?>
                    {
                        ["previousAttempts"] = previousAttempts,
                        ["previousDeadLetteredAt"] = previousDeadLetteredAt,
                        ["messageType"] = message.Type
                    });

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completed = true;
                RedriveCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));

                return new RedriveOutboxResponse
                {
                    Id = message.Id,
                    ReDriven = true,
                    NextAttemptAt = now
                };
            }
            catch
            {
                if (!completed)
                    await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<PagedResult<AuditEventResponse>> GetAuditEventsAsync(
            AuditQueryParams query,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(query.Page, query.PageSize, defaultSize: 20);
            var events = _context.AuditEvents.AsNoTracking().AsQueryable();

            if (query.ActorUserId.HasValue)
                events = events.Where(item => item.ActorUserId == query.ActorUserId);
            if (!string.IsNullOrWhiteSpace(query.Action))
                events = events.Where(item => item.Action == query.Action.Trim());
            if (!string.IsNullOrWhiteSpace(query.EntityType))
                events = events.Where(item => item.EntityType == query.EntityType.Trim());
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

            return PagedResult<AuditEventResponse>.Create(items, totalCount, paging.Page, paging.Size);
        }
    }
}
