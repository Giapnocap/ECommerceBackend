namespace ECommerceBackend.Application.DTOs
{
    public sealed class PaymentMethodResponse
    {
        public string Method { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public bool SupportsWebhooks { get; set; }
        public bool RequiresExternalInitialization { get; set; }
    }

    public sealed class PaymentWebhookResponse
    {
        public string EventId { get; set; } = string.Empty;
        public Guid PaymentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool Duplicate { get; set; }
    }

    public sealed class ExternalPaymentResponse
    {
        public Guid PaymentId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ProviderPaymentId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ClientSecret { get; set; }
    }
}
