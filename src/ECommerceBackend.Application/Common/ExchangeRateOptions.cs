namespace ECommerceBackend.Application.Common
{
    public sealed class ExchangeRateOptions
    {
        public const string SectionName = "Pricing:ExchangeRates";

        public bool Enabled { get; set; }
        public string BaseUrl { get; set; } =
            "https://api.currencyapi.com/";
        public string ApiKey { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 10;
        public int CacheMinutes { get; set; } = 60;
        public int MaxStaleMinutes { get; set; } = 1440;
    }
}
