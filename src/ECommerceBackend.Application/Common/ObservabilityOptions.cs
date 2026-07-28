namespace ECommerceBackend.Application.Common
{
    public sealed class ObservabilityOptions
    {
        public const string SectionName = "Observability";

        public bool Enabled { get; set; } = true;
        public string ServiceName { get; set; } = "ECommerceBackend";
        public double TraceSamplingRatio { get; set; } = 1;
        public OtlpOptions Otlp { get; set; } = new();
    }

    public sealed class OtlpOptions
    {
        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = "http://localhost:4317";
    }
}
