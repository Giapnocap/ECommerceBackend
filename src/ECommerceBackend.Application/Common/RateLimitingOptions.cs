namespace ECommerceBackend.Application.Common
{
    public sealed class RateLimitingOptions
    {
        public const string SectionName = "RateLimiting";

        public FixedWindowRateLimitPolicyOptions Auth { get; set; } = new()
        {
            PermitLimit = 10
        };

        public FixedWindowRateLimitPolicyOptions Refresh { get; set; } = new()
        {
            PermitLimit = 30
        };

        public FixedWindowRateLimitPolicyOptions Upload { get; set; } = new()
        {
            PermitLimit = 20
        };

        public FixedWindowRateLimitPolicyOptions Webhook { get; set; } = new()
        {
            PermitLimit = 120
        };

        public FixedWindowRateLimitPolicyOptions Checkout { get; set; } = new()
        {
            PermitLimit = 5
        };
    }

    public sealed class FixedWindowRateLimitPolicyOptions
    {
        public int PermitLimit { get; set; }
        public int WindowSeconds { get; set; } = 60;
    }
}
