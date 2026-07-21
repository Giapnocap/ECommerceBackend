namespace ECommerceBackend.Application.Common
{
    public sealed class PaymentWebhookOptions
    {
        public const string SectionName = "PaymentWebhooks:GenericHmac";
        public const int MinimumSecretBytes = 32;

        public bool Enabled { get; set; }
        public string ProviderCode { get; set; } = "generic-hmac";
        public string Secret { get; set; } = string.Empty;
        public int MaxPayloadBytes { get; set; } = 65_536;
        public int MaxFutureSkewMinutes { get; set; } = 5;
    }
}
