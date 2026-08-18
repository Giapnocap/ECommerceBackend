namespace ECommerceBackend.Application.Common
{
    public sealed class StripePaymentOptions
    {
        public const string SectionName = "Payments:Stripe";

        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } = "https://api.stripe.com/";
        public string SecretKey { get; set; } = string.Empty;
        public string PublishableKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 15;
        public int CreationLeaseSeconds { get; set; } = 120;
        public int WebhookToleranceSeconds { get; set; } = 300;
        public bool ReconciliationEnabled { get; set; }
        public bool RequireReconciliation { get; set; }
        public int ReconciliationPollIntervalSeconds { get; set; } = 60;
        public int ReconciliationStaleAfterMinutes { get; set; } = 5;
        public int ReconciliationBatchSize { get; set; } = 50;
    }
}
