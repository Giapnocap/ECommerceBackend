using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IPaymentRepository
    {
        Task<PaymentStatusHistory?> GetRefundHistoryAsync(
            Guid paymentId,
            CancellationToken cancellationToken = default);

        void Add(Payment payment);

        void AddStatusHistory(PaymentStatusHistory history);
    }
}
