using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Common
{
    public sealed record DataRetentionBatch(
        IReadOnlyList<OutboxMessage> ProcessedOutbox,
        IReadOnlyList<RefreshToken> ExpiredRefreshTokens,
        IReadOnlyList<PaymentWebhookEvent> WebhookPayloads);
}
