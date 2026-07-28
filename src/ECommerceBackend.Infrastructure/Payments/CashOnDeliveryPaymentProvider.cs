using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Infrastructure.Payments
{
    public sealed class CashOnDeliveryPaymentProvider : IPaymentProvider
    {
        public string Code => "cod";
        public PaymentMethod? CheckoutMethod => PaymentMethod.CashOnDelivery;
        public bool SupportsWebhooks => false;

        public PaymentInitializationResult Initialize(PaymentInitializationRequest request)
            => new(PaymentStatusTransitions.Initial, Code);

        public Task<VerifiedPaymentWebhook> VerifyWebhookAsync(
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default)
            => throw new BusinessException("Thanh toán khi nhận hàng không hỗ trợ thông báo từ cổng thanh toán.");
    }
}
