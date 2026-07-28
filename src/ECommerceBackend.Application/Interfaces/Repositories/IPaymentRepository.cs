using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IPaymentRepository
    {
        Task<Guid?> GetOrderIdByProviderTransactionAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken = default);

        Task<PaymentWebhookEvent?> GetWebhookEventAsync(
            string provider,
            string eventId,
            CancellationToken cancellationToken = default);

        Task<PaymentStatusHistory?> GetRefundHistoryAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default);

        void Add(Payment payment);

        void AddWebhookEvent(PaymentWebhookEvent webhookEvent);

        void AddStatusHistory(PaymentStatusHistory history);
    }
}
