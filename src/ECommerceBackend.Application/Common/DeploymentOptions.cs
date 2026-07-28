namespace ECommerceBackend.Application.Common
{
    public sealed class ReverseProxyOptions
    {
        public const string SectionName = "ReverseProxy";

        public bool Enabled { get; set; }
        public int ForwardLimit { get; set; } = 1;
        public bool RequireHeaderSymmetry { get; set; } = true;
        public string[] KnownProxies { get; set; } = [];
        public string[] KnownNetworks { get; set; } = [];
    }

    public sealed class DataProtectionStorageOptions
    {
        public const string SectionName = "DataProtection";

        public string ApplicationName { get; set; } = "ECommerceBackend";
        public string KeysPath { get; set; } = string.Empty;
    }

    public sealed class ProductionSecurityOptions
    {
        public const string OptionsName = "ProductionSecurity";

        public string ConnectionString { get; set; } = string.Empty;
        public string AllowedHosts { get; set; } = string.Empty;
        public bool IsProduction { get; set; }
    }
}
