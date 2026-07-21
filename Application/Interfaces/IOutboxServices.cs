using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IOutboxWriter
    {
        void EnqueueNotification(
            Guid userId,
            string subject,
            string message,
            Guid? orderId = null,
            Guid? paymentId = null);
    }

    public interface IOutboxStore
    {
        Task<IReadOnlyList<OutboxMessage>> ClaimBatchAsync(
            Guid lockId,
            int batchSize,
            DateTime now,
            DateTime staleBefore,
            CancellationToken cancellationToken = default);

        Task<bool> MarkProcessedAsync(
            Guid messageId,
            Guid lockId,
            DateTime processedAt,
            CancellationToken cancellationToken = default);

        Task<bool> MarkFailedAsync(
            Guid messageId,
            Guid lockId,
            int attempts,
            DateTime nextAttemptAt,
            DateTime? deadLetteredAt,
            string error,
            CancellationToken cancellationToken = default);
    }

    public interface IOutboxMessageHandler
    {
        Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    }

    public interface INotificationSender
    {
        Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default);
    }
}
