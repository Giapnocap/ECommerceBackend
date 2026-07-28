namespace ECommerceBackend.Application.Common
{
    public sealed class DatabaseOptions
    {
        public const string SectionName = "Database";

        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
