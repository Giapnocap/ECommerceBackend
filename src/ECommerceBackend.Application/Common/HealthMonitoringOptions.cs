namespace ECommerceBackend.Application.Common
{
    public sealed class HealthMonitoringOptions
    {
        public const string SectionName = "HealthChecks";

        public int DependencyTimeoutSeconds { get; set; } = 5;
    }
}
