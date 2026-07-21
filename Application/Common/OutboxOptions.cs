namespace ECommerceBackend.Application.Common
{
    public sealed class OutboxOptions
    {
        public const string SectionName = "Outbox";

        public bool Enabled { get; set; } = true;
        public int PollIntervalSeconds { get; set; } = 5;
        public int BatchSize { get; set; } = 20;
        public int MaxAttempts { get; set; } = 5;
        public int LockTimeoutMinutes { get; set; } = 5;
        public int ProcessingTimeoutSeconds { get; set; } = 60;
        public int MaxPendingAgeMinutes { get; set; } = 15;
    }

    public sealed class SmtpOptions
    {
        public const string SectionName = "Notifications:Smtp";

        public bool Enabled { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public int TimeoutSeconds { get; set; } = 30;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string FromAddress { get; set; } = string.Empty;
        public string FromName { get; set; } = "ECommerceBackend";
    }
}
