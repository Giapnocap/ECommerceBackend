using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces
{
    public sealed record PaymentInitializationRequest(
        Guid OrderId,
        string OrderNumber,
        decimal Amount);

    public sealed record PaymentInitializationResult(
        PaymentStatus Status,
        string? Provider,
        string? ProviderTransactionId = null);

    public sealed record PaymentWebhookRequest(
        string EventId,
        string Signature,
        string Payload);

    public sealed record VerifiedPaymentWebhook(
        string ProviderTransactionId,
        PaymentStatus Status,
        DateTime OccurredAt,
        decimal? Amount);

    public sealed record PaymentCheckoutCapability(
        PaymentMethod Method,
        string ProviderCode,
        bool SupportsWebhooks);

    public interface IPaymentProvider
    {
        string Code { get; }
        PaymentMethod? CheckoutMethod { get; }
        bool SupportsWebhooks { get; }

        // Must be local and side-effect free because checkout holds database row locks.
        PaymentInitializationResult Initialize(PaymentInitializationRequest request);
        Task<VerifiedPaymentWebhook> VerifyWebhookAsync(
            PaymentWebhookRequest request,
            CancellationToken cancellationToken = default);
    }

    public interface IPaymentProviderResolver
    {
        IPaymentProvider GetCheckoutProvider(PaymentMethod method);
        IPaymentProvider GetWebhookProvider(string providerCode);
        IReadOnlyList<PaymentCheckoutCapability> GetCheckoutCapabilities();
    }
}
