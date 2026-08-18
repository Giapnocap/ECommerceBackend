using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IPaymentCommandService
    {
        Task<ExternalPaymentResponse> InitializeExternalPaymentAsync(
            Guid orderId,
            Guid actorUserId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default);
    }
}
