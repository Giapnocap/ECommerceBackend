using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class DataRetentionRepository : IDataRetentionRepository
    {
        private readonly AppDbContext _context;

        public DataRetentionRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DataRetentionBatch> LoadCandidatesAsync(
            int batchSize,
            DateTime processedOutboxCutoff,
            DateTime expiredRefreshTokenCutoff,
            DateTime webhookPayloadCutoff,
            CancellationToken cancellationToken = default)
        {
            var processedOutbox = await _context.OutboxMessages
                .Where(message => message.ProcessedAt != null
                    && message.ProcessedAt < processedOutboxCutoff)
                .OrderBy(message => message.ProcessedAt)
                .ThenBy(message => message.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            var expiredRefreshTokens = await _context.RefreshTokens
                .Where(token => token.ExpiresAt < expiredRefreshTokenCutoff)
                .OrderBy(token => token.ExpiresAt)
                .ThenBy(token => token.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);
            var webhookPayloads = await _context.PaymentWebhookEvents
                .Where(webhook => webhook.ReceivedAt < webhookPayloadCutoff
                    && webhook.Payload != string.Empty)
                .OrderBy(webhook => webhook.ReceivedAt)
                .ThenBy(webhook => webhook.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            return new DataRetentionBatch(
                processedOutbox,
                expiredRefreshTokens,
                webhookPayloads);
        }

        public void Apply(DataRetentionBatch batch)
        {
            _context.OutboxMessages.RemoveRange(batch.ProcessedOutbox);
            _context.RefreshTokens.RemoveRange(batch.ExpiredRefreshTokens);
            foreach (var webhook in batch.WebhookPayloads)
                webhook.Payload = string.Empty;
        }
    }
}
