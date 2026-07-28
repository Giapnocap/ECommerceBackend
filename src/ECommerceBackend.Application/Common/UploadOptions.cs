namespace ECommerceBackend.Application.Common
{
    public sealed class UploadOptions
    {
        public const string SectionName = "Uploads";
        public const long DefaultMaxImageSizeBytes = 5 * 1024 * 1024;

        public long MaxImageSizeBytes { get; set; } = DefaultMaxImageSizeBytes;
        public int ReconciliationGraceMinutes { get; set; } = 60;
        public int MaxReconciliationDeletes { get; set; } = 100;
    }
}
