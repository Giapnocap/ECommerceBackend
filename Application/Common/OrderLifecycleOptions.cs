namespace ECommerceBackend.Application.Common
{
    public sealed class OrderLifecycleOptions
    {
        public const string SectionName = "OrderLifecycle";

        public int PendingCodHoldMinutes { get; set; } = 30;
        public int MaxPendingOrdersPerCustomer { get; set; } = 3;
        public bool ExpirationEnabled { get; set; }
        public bool ExpirationDryRun { get; set; } = true;
        public int ExpirationPollIntervalSeconds { get; set; } = 30;
        public int ExpirationBatchSize { get; set; } = 50;
        public int MaxOverdueMinutes { get; set; } = 15;
    }
}
