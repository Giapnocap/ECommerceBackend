namespace ECommerceBackend.Application.Common
{
    public sealed class PricingOptions
    {
        public const string SectionName = "Pricing";

        public string Currency { get; set; } = "VND";
        public int QuoteValidityMinutes { get; set; } = 5;
        public decimal StandardShippingFee { get; set; } = 30_000m;
        public decimal ExpressShippingFee { get; set; } = 60_000m;
        public decimal FreeStandardShippingMinimum { get; set; } = 1_000_000m;
        public decimal TaxRatePercent { get; set; }
    }
}
