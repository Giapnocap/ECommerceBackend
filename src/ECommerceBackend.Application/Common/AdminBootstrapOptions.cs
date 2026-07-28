namespace ECommerceBackend.Application.Common
{
    public sealed class AdminBootstrapOptions
    {
        public const string SectionName = "AdminBootstrap";

        public bool Enabled { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = "System Administrator";
        public string? Phone { get; set; }
    }
}
