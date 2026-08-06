using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IPaymentWebhookService
    {
        Task<PaymentWebhookResponse> HandleAsync(
            string providerCode,
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default);
    }
}
