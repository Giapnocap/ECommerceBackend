using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Application.DTOs
{
    public sealed class DashboardSummaryQuery
    {
        public int LowStockThreshold { get; set; } = 10;
    }

    public sealed class DashboardSummaryResponse
    {
        public DateTime GeneratedAt { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public decimal RevenueToday { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public int OrdersToday { get; set; }
        public int TotalOrders { get; set; }
        public int PendingOrderCount { get; set; }
        public int CompletedOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }
        public int OpenReturnRequestCount { get; set; }
        public int TotalCustomerCount { get; set; }
        public int NewCustomerCountThisMonth { get; set; }
        public int LowStockThreshold { get; set; }
        public int LowStockProductCount { get; set; }
    }

    public sealed class DashboardRevenueQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public string GroupBy { get; set; } = "day";
    }

    public sealed class DashboardRevenueTrendResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string GroupBy { get; set; } = string.Empty;
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public IReadOnlyList<DashboardRevenuePointResponse> Items { get; set; } = [];
    }

    public sealed class DashboardRevenuePointResponse
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal GrossRevenue { get; set; }
        public decimal RefundedAmount { get; set; }
        public decimal NetRevenue { get; set; }
    }

    public sealed class DashboardTopProductsQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int Limit { get; set; } = 10;
    }

    public sealed class DashboardRecentActivitiesQuery
    {
        public int Limit { get; set; } = 10;
    }

    public sealed class DashboardRecentActivityResponse
    {
        public string Type { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public Guid? ActorUserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public sealed class DashboardSummaryMetrics
    {
        public decimal RevenueToday { get; set; }
        public decimal RevenueThisMonth { get; set; }
        public int OrdersToday { get; set; }
        public int TotalOrders { get; set; }
        public int PendingOrderCount { get; set; }
        public int CompletedOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }
        public int OpenReturnRequestCount { get; set; }
        public int TotalCustomerCount { get; set; }
        public int NewCustomerCountThisMonth { get; set; }
        public int LowStockProductCount { get; set; }
    }

}
