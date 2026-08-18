using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public sealed record PaymentReconciliationCandidate(
        Guid PaymentId,
        Guid OrderId,
        string Provider,
        string ProviderPaymentId,
        decimal Amount,
        string Currency);

    public interface IPaymentRepository
    {
        Task<IReadOnlyList<PaymentReconciliationCandidate>>
            GetStaleExternalPaymentsAsync(
                DateTime staleBefore,
                int batchSize,
                CancellationToken cancellationToken = default);

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

        Task<PaymentMethod?> GetMethodByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default);

        Task<PaymentRefund?> GetRefundAsync(
            Guid paymentId,
            string idempotencyKey,
            bool tracking,
            CancellationToken cancellationToken = default);

        Task<decimal> GetReservedRefundAmountAsync(
            Guid paymentId,
            Guid? excludedRefundId,
            CancellationToken cancellationToken = default);

        Task<decimal> GetReservedRefundBaseAmountAsync(
            Guid paymentId,
            Guid? excludedRefundId,
            CancellationToken cancellationToken = default);

        Task<decimal> GetSucceededRefundBaseAmountAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default);

        void Add(Payment payment);

        void AddWebhookEvent(PaymentWebhookEvent webhookEvent);

        void AddRefund(PaymentRefund refund);

        void AddStatusHistory(PaymentStatusHistory history);
    }
}
