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

        public void AddStatusHistory(PaymentStatusHistory history)
            => _context.PaymentStatusHistories.Add(history);
    }
}
