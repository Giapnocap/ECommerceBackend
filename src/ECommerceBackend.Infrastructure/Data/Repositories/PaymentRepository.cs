using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class PaymentRepository : IPaymentRepository
    {
        private readonly AppDbContext _context;

        public PaymentRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<Guid?> GetOrderIdByProviderTransactionAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken = default)
            => _context.Payments
                .AsNoTracking()
                .Where(payment => payment.Provider == provider
                    && payment.ProviderTransactionId == providerTransactionId)
                .Select(payment => (Guid?)payment.OrderId)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<PaymentWebhookEvent?> GetWebhookEventAsync(
            string provider,
            string eventId,
            CancellationToken cancellationToken = default)
            => _context.PaymentWebhookEvents
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    webhook => webhook.Provider == provider
                        && webhook.ProviderEventId == eventId,
                    cancellationToken);

        public Task<PaymentStatusHistory?> GetRefundHistoryAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
            => _context.PaymentStatusHistories
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    history => history.PaymentId == paymentId
                        && history.ToStatus == PaymentStatus.Refunded,
                    cancellationToken);

        public void Add(Payment payment)
            => _context.Payments.Add(payment);

        public void AddWebhookEvent(PaymentWebhookEvent webhookEvent)
            => _context.PaymentWebhookEvents.Add(webhookEvent);

        public void AddStatusHistory(PaymentStatusHistory history)
            => _context.PaymentStatusHistories.Add(history);
    }
}
