using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class OutboxRepository : IOutboxRepository
    {
        private readonly AppDbContext _context;

        public OutboxRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PageSlice<DeadLetterResponse>> GetDeadLettersAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var messages = _context.OutboxMessages
                .AsNoTracking()
                .Where(message => message.DeadLetteredAt != null);
            var totalCount = await messages.CountAsync(cancellationToken);
            var items = await messages
                .OrderByDescending(message => message.DeadLetteredAt)
                .ThenBy(message => message.Id)
                .Skip(skip)
                .Take(take)
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

            return new PageSlice<DeadLetterResponse>(items, totalCount);
        }

        public void Add(OutboxMessage message)
            => _context.OutboxMessages.Add(message);
    }
}
