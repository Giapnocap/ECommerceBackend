using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;

namespace ECommerceBackend.Application.Services
{
    public sealed class PaymentCommandService : IPaymentCommandService
    {
        private readonly ExternalPaymentCreationUseCase _externalCreation;

        public PaymentCommandService(
            ExternalPaymentCreationUseCase externalCreation)
        {
            _externalCreation = externalCreation;
        }

        public Task<ExternalPaymentResponse> InitializeExternalPaymentAsync(
            Guid orderId,
            Guid actorUserId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
            => _externalCreation.ExecuteAsync(
                orderId,
                actorUserId,
                canProcessOrders,
                cancellationToken);
    }
}
