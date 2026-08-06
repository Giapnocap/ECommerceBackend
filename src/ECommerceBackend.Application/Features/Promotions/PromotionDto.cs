using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.DTOs
{
    public interface IPromotionRuleRequest
    {
        PromotionType Type { get; }
        decimal Value { get; }
        decimal MinimumSubtotal { get; }
        decimal? MaximumDiscountAmount { get; }
        DateTime StartsAt { get; }
        DateTime EndsAt { get; }
        int UsageLimit { get; }
        int UsageLimitPerCustomer { get; }
    }

    public sealed class PromotionResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public decimal Value { get; set; }
        public decimal MinimumSubtotal { get; set; }
        public decimal? MaximumDiscountAmount { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int UsageLimit { get; set; }
        public int UsageLimitPerCustomer { get; set; }
        public int UsedCount { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public sealed class CreatePromotionRequest : IPromotionRuleRequest
    {
        public string Code { get; set; } = string.Empty;
        public PromotionType Type { get; set; }
        public decimal Value { get; set; }
        public decimal MinimumSubtotal { get; set; }
        public decimal? MaximumDiscountAmount { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int UsageLimit { get; set; }
        public int UsageLimitPerCustomer { get; set; } = 1;
    }

    public sealed class UpdatePromotionRequest : IPromotionRuleRequest
    {
        public PromotionType Type { get; set; }
        public decimal Value { get; set; }
        public decimal MinimumSubtotal { get; set; }
        public decimal? MaximumDiscountAmount { get; set; }
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int UsageLimit { get; set; }
        public int UsageLimitPerCustomer { get; set; } = 1;
        public bool IsActive { get; set; } = true;
    }

    public sealed class PromotionQueryParams
    {
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }
}
