using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data
{
    public sealed class EfOutboxStore : IOutboxStore
    {
        private readonly AppDbContext _context;

        public EfOutboxStore(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
            Guid lockId,
            int batchSize,
            DateTime now,
            DateTime staleBefore,
            CancellationToken cancellationToken = default)
        {
            var candidates = await _context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.ProcessedAt == null
                    && message.DeadLetteredAt == null
                    && message.NextAttemptAt <= now
                    && (message.LockedAt == null || message.LockedAt < staleBefore))
                .OrderBy(message => message.NextAttemptAt)
                .ThenBy(message => message.OccurredAt)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .Select(message => message.Id)
                .ToListAsync(cancellationToken);

            if (!_context.Database.IsRelational())
            {
                var fallbackMessages = await _context.OutboxMessages
                    .Where(message => candidates.Contains(message.Id)
                        && message.ProcessedAt == null
                        && message.DeadLetteredAt == null
                        && message.NextAttemptAt <= now
                        && (message.LockedAt == null || message.LockedAt < staleBefore))
                    .OrderBy(message => message.NextAttemptAt)
                    .ThenBy(message => message.OccurredAt)
                    .ThenBy(message => message.Id)
                    .ToListAsync(cancellationToken);

                foreach (var message in fallbackMessages)
                {
                    message.LockId = lockId;
                    message.LockedAt = now;
                    message.LastAttemptAt = now;
                }

                await _context.SaveChangesAsync(cancellationToken);
                return fallbackMessages;
            }

            var claimedIds = new List<Guid>(candidates.Count);
            foreach (var candidateId in candidates)
            {
                var affected = await _context.OutboxMessages
                    .Where(message => message.Id == candidateId
                        && message.ProcessedAt == null
                        && message.DeadLetteredAt == null
                        && message.NextAttemptAt <= now
                        && (message.LockedAt == null || message.LockedAt < staleBefore))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(message => message.LockId, lockId)
                        .SetProperty(message => message.LockedAt, now)
                        .SetProperty(message => message.LastAttemptAt, now), cancellationToken);

                if (affected == 1)
                    claimedIds.Add(candidateId);
            }

            return await _context.OutboxMessages
                .AsNoTracking()
                .Where(message => claimedIds.Contains(message.Id) && message.LockId == lockId)
                .OrderBy(message => message.NextAttemptAt)
                .ThenBy(message => message.OccurredAt)
                .ThenBy(message => message.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<bool> MarkProcessedAsync(
            Guid messageId,
            Guid lockId,
            DateTime processedAt,
            CancellationToken cancellationToken = default)
        {
            if (!_context.Database.IsRelational())
            {
                var message = await _context.OutboxMessages.SingleOrDefaultAsync(
                    candidate => candidate.Id == messageId && candidate.LockId == lockId,
                    cancellationToken);
                if (message is null)
                    return false;

                message.ProcessedAt = processedAt;
                message.LockId = null;
                message.LockedAt = null;
                message.LastError = null;
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }

            var affected = await _context.OutboxMessages
                .Where(message => message.Id == messageId && message.LockId == lockId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.ProcessedAt, processedAt)
                    .SetProperty(message => message.LockId, (Guid?)null)
                    .SetProperty(message => message.LockedAt, (DateTime?)null)
                    .SetProperty(message => message.LastError, (string?)null), cancellationToken);

            return affected == 1;
        }

        public async Task<bool> MarkFailedAsync(
            Guid messageId,
            Guid lockId,
            int attempts,
            DateTime nextAttemptAt,
            DateTime? deadLetteredAt,
            string error,
            CancellationToken cancellationToken = default)
        {
            if (!_context.Database.IsRelational())
            {
                var message = await _context.OutboxMessages.SingleOrDefaultAsync(
                    candidate => candidate.Id == messageId && candidate.LockId == lockId,
                    cancellationToken);
                if (message is null)
                    return false;

                message.Attempts = attempts;
                message.NextAttemptAt = nextAttemptAt;
                message.DeadLetteredAt = deadLetteredAt;
                message.LastError = error;
                message.LockId = null;
                message.LockedAt = null;
                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }

            var affected = await _context.OutboxMessages
                .Where(message => message.Id == messageId && message.LockId == lockId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(message => message.Attempts, attempts)
                    .SetProperty(message => message.NextAttemptAt, nextAttemptAt)
                    .SetProperty(message => message.DeadLetteredAt, deadLetteredAt)
                    .SetProperty(message => message.LastError, error)
                    .SetProperty(message => message.LockId, (Guid?)null)
                    .SetProperty(message => message.LockedAt, (DateTime?)null), cancellationToken);

            return affected == 1;
        }
    }
}