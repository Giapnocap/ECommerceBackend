namespace ECommerceBackend.Application.Common
{
    public sealed class DataRetentionOptions
    {
        public const string SectionName = "DataRetention";

        // Deletion remains opt-in even when an administrator requests an applied run.
        public bool Enabled { get; set; }
        public bool AutomaticProcessingEnabled { get; set; }
        public bool RequireAutomaticProcessing { get; set; }
        public int ProcessedOutboxRetentionDays { get; set; } = 30;
        public int ExpiredRefreshTokenRetentionDays { get; set; } = 30;
        public int WebhookPayloadRetentionDays { get; set; } = 30;
        public int MaxBatchSize { get; set; } = 100;
        public int ProcessingIntervalMinutes { get; set; } = 1440;
        public int FailureRetryMinutes { get; set; } = 5;
        public int MaxBatchesPerCycle { get; set; } = 10;
    }
}
