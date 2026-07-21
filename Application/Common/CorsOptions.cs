namespace ECommerceBackend.Application.Common
{
    public sealed class CorsOptions
    {
        public const string SectionName = "Cors";

        public string[] AllowedOrigins { get; set; } = [];
    }
}
