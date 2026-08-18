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

        public async Task<IReadOnlyList<PaymentReconciliationCandidate>>
            GetStaleExternalPaymentsAsync(
                DateTime staleBefore,
                int batchSize,
                CancellationToken cancellationToken = default)
            => await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.Method == PaymentMethod.Card
                    && payment.Provider != null
                    && payment.ProviderTransactionId != null
                    && (payment.Status == PaymentStatus.Pending
                        || payment.Status == PaymentStatus.RequiresAction
                        || payment.Status == PaymentStatus.Processing)
                    && (payment.LastReconciledAt
                            ?? payment.LastProviderEventAt
                            ?? payment.ExternalCreatedAt
                            ?? payment.CreatedAt) <= staleBefore)
                .OrderBy(payment => payment.LastReconciledAt
                    ?? payment.LastProviderEventAt
                    ?? payment.ExternalCreatedAt
                    ?? payment.CreatedAt)
                .ThenBy(payment => payment.Id)
                .Select(payment => new PaymentReconciliationCandidate(
                    payment.Id,
                    payment.OrderId,
                    payment.Provider!,
                    payment.ProviderTransactionId!,
                    payment.Amount,
                    payment.Currency))
                .Take(batchSize)
                .ToListAsync(cancellationToken);

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

        public Task<PaymentMethod?> GetMethodByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
            => _context.Payments
                .AsNoTracking()
                .Where(payment => payment.OrderId == orderId)
                .Select(payment => (PaymentMethod?)payment.Method)
                .SingleOrDefaultAsync(cancellationToken);

        public Task<PaymentRefund?> GetRefundAsync(
            Guid paymentId,
            string idempotencyKey,
            bool tracking,
            CancellationToken cancellationToken = default)
        {
            IQueryable<PaymentRefund> query = _context.PaymentRefunds;
            if (!tracking)
                query = query.AsNoTracking();

            return query.SingleOrDefaultAsync(
                refund => refund.PaymentId == paymentId
                    && refund.IdempotencyKey == idempotencyKey,
                cancellationToken);
        }

        public async Task<decimal> GetReservedRefundAmountAsync(
            Guid paymentId,
            Guid? excludedRefundId,
            CancellationToken cancellationToken = default)
            => await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.PaymentId == paymentId
                    && refund.Id != excludedRefundId
                    && (refund.Status == PaymentRefundStatus.Pending
                        || refund.Status == PaymentRefundStatus.Processing))
                .SumAsync(
                    refund => (decimal?)refund.Amount,
                cancellationToken)
                ?? 0m;

        public async Task<decimal> GetReservedRefundBaseAmountAsync(
            Guid paymentId,
            Guid? excludedRefundId,
            CancellationToken cancellationToken = default)
            => await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.PaymentId == paymentId
                    && refund.Id != excludedRefundId
                    && (refund.Status == PaymentRefundStatus.Pending
                        || refund.Status == PaymentRefundStatus.Processing))
                .SumAsync(
                    refund => (decimal?)refund.BaseAmount,
                    cancellationToken)
                ?? 0m;

        public async Task<decimal> GetSucceededRefundBaseAmountAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default)
            => await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.PaymentId == paymentId
                    && refund.Status == PaymentRefundStatus.Succeeded)
                .SumAsync(
                    refund => (decimal?)refund.BaseAmount,
                    cancellationToken)
                ?? 0m;

        public void Add(Payment payment)
            => _context.Payments.Add(payment);

        public void AddWebhookEvent(PaymentWebhookEvent webhookEvent)
            => _context.PaymentWebhookEvents.Add(webhookEvent);

        public void AddRefund(PaymentRefund refund)
            => _context.PaymentRefunds.Add(refund);

        public void AddStatusHistory(PaymentStatusHistory history)
            => _context.PaymentStatusHistories.Add(history);
    }
}
