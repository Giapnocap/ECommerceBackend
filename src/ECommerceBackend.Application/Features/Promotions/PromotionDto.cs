using ECommerceBackend.Domain.Common;
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
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
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

    public class PromotionAnalyticsRangeQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public sealed class PromotionAnalyticsQuery : PromotionAnalyticsRangeQuery
    {
        public string SortBy { get; set; } = "usage";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class PromotionAnalyticsResponse
    {
        public Guid PromotionId { get; set; }
        public string Code { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime StartsAt { get; set; }
        public DateTime EndsAt { get; set; }
        public int UsageCount { get; set; }
        public int GeneratedOrderCount { get; set; }
        public decimal GrossRevenue { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal NetRevenue { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
    }
}
