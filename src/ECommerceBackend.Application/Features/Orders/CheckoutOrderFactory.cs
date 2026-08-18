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
            CheckoutRecipient recipient,
            string idempotencyKey,
            string requestHash,
            OrderPricingCalculation pricing,
            DateTime occurredAt)
        {
            var order = DomainRuleGuard.AsBusiness(() =>
                Order.Create(
                    Guid.NewGuid(),
                    userId,
                    CreateOrderNumber(occurredAt),
                    idempotencyKey,
                    requestHash,
                    pricing.Promotion?.Id,
                    pricing.Promotion?.Code,
                    request.ShippingMethod,
                    pricing.Currency,
                    occurredAt,
                    request.ShippingAddress,
                    request.Note));
            DomainRuleGuard.AsBusiness(() =>
                order.SetRecipient(
                    recipient.Name,
                    recipient.Phone));
            DomainRuleGuard.AsBusiness(() =>
                order.SetPricingSnapshot(
                    pricing.BaseCurrency,
                    pricing.ExchangeRate,
                    pricing.ExchangeRateCapturedAt,
                    pricing.BaseAmounts,
                    pricing.Amounts));
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
            var payment = DomainRuleGuard.AsBusiness(() =>
                Payment.Create(
                    Guid.NewGuid(),
                    order.Id,
                    request.PaymentMethod,
                    order.TotalAmount,
                    initialized.Provider,
                    initialized.ProviderTransactionId,
                    occurredAt,
                    pricing.Currency));
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

    internal sealed record CheckoutRecipient(
        string Name,
        string? Phone);
}
