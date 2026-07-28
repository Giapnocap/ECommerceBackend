namespace ECommerceBackend.Application.Common
{
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";
        public const int MinimumKeyBytes = 32;

        public string Key { get; set; } = string.Empty;
        public string Issuer { get; set; } = "ECommerceBackend";
        public string Audience { get; set; } = "ECommerceBackend.Client";
        public int AccessTokenMinutes { get; set; } = 60;
        public int RefreshTokenDays { get; set; } = 7;
    }
}
