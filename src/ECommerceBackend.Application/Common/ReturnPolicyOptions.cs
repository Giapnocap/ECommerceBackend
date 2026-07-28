namespace ECommerceBackend.Application.Common
{
    public sealed class ReturnPolicyOptions
    {
        public const string SectionName = "Returns";

        public int ReturnWindowDays { get; set; } = 14;
    }
}
