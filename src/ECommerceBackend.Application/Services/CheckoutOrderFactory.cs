using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class CheckoutOrderFactory
    {
        private readonly IPaymentProviderResolver _paymentProviders;
        private readonly OrderLifecycleOptions _options;

        public CheckoutOrderFactory(
            IPaymentProviderResolver paymentProviders,
            IOptions<OrderLifecycleOptions> options)
        {
            _paymentProviders = paymentProviders;
            _options = options.Value;
        }

        internal CheckoutOrderCreation Create(
            Guid userId,
            PlaceOrderRequest request,
            string idempotencyKey,
            string requestHash,
            OrderPricingCalculation pricing,
            DateTime occurredAt)
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OrderNumber = CreateOrderNumber(occurredAt),
                IdempotencyKey = idempotencyKey,
                IdempotencyRequestHash = requestHash,
                PromotionId = pricing.Promotion?.Id,
                PromotionCodeSnapshot = pricing.Promotion?.Code,
                ShippingMethod = request.ShippingMethod,
                Currency = pricing.Currency,
                OrderDate = occurredAt,
                ShippingAddress = request.ShippingAddress.Trim(),
                Note = CheckoutRequestIdentity.NormalizeOptional(
                    request.Note)
            };
            DomainRuleGuard.AsBusiness(() =>
                order.SetPricing(
                    pricing.Amounts.Subtotal,
                    pricing.Amounts.Discount,
                    pricing.Amounts.Shipping,
                    pricing.Amounts.Tax));
            DomainRuleGuard.AsBusiness(() =>
                order.SetPendingExpiration(
                    occurredAt.AddMinutes(
                        _options.PendingCodHoldMinutes)));

            var provider = _paymentProviders.GetCheckoutProvider(
                request.PaymentMethod);
            var initialized = PaymentProviderContract.NormalizeInitialization(
                provider,
                provider.Initialize(
                    new PaymentInitializationRequest(
                        order.Id,
                        order.OrderNumber,
                        order.TotalAmount)));
            var payment = new Payment
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Method = request.PaymentMethod,
                Amount = order.TotalAmount,
                Provider = initialized.Provider,
                ProviderTransactionId =
                    initialized.ProviderTransactionId,
                CreatedAt = occurredAt
            };
            if (initialized.Status != payment.Status)
            {
                DomainRuleGuard.AsBusiness(() =>
                    payment.ChangeStatus(
                        initialized.Status,
                        occurredAt));
            }

            return new CheckoutOrderCreation(order, payment);
        }

        private static string CreateOrderNumber(DateTime occurredAt)
            => $"ORD-{occurredAt:yyyyMMdd}-{Guid.NewGuid():N}"
                [..32]
                .ToUpperInvariant();
    }

    internal sealed record CheckoutOrderCreation(
        Order Order,
        Payment Payment);
}
