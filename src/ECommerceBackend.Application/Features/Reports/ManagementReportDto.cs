using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Application.DTOs
{
    public abstract class ReportDateRangeQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
    }

    public sealed class RevenueReportQuery : ReportDateRangeQuery
    {
        public string GroupBy { get; set; } = "day";
    }

    public sealed class OrderReportQuery : ReportDateRangeQuery
    {
    }

    public sealed class ProductReportQuery : ReportDateRangeQuery
    {
        public int Limit { get; set; } = 10;
        public int LowStockThreshold { get; set; } = 10;
    }

    public sealed class CustomerReportQuery : ReportDateRangeQuery
    {
        public int Limit { get; set; } = 10;
    }

    public sealed class ReturnReportQuery : ReportDateRangeQuery
    {
        public int ReasonLimit { get; set; } = 10;
    }

    public sealed class RevenueReportResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string GroupBy { get; set; } = string.Empty;
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public decimal GrossRevenue { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal NetRevenue { get; set; }
        public int OrderCount { get; set; }
        public decimal AverageOrderValue { get; set; }
        public IReadOnlyList<RevenueReportPointResponse> Trend { get; set; } = [];
    }

    public sealed class RevenueReportPointResponse
    {
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public decimal GrossRevenue { get; set; }
        public decimal RefundAmount { get; set; }
        public decimal NetRevenue { get; set; }
        public int OrderCount { get; set; }
    }

    public sealed class OrderReportResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public int TotalOrders { get; set; }
        public int PendingOrders { get; set; }
        public int DeliveredOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int ReturnedOrders { get; set; }
        public decimal CompletionRatePercent { get; set; }
        public decimal CancellationRatePercent { get; set; }
        public decimal ReturnRatePercent { get; set; }
        public IReadOnlyList<StatusBreakdownResponse> OrdersByStatus { get; set; } = [];
    }

    public sealed class ProductReportResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public int LowStockThreshold { get; set; }
        public int LowStockProductCount { get; set; }
        public IReadOnlyList<TopSellingProductResponse> TopSellingProducts { get; set; } = [];
    }

    public sealed class CustomerReportResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public int NewCustomerCount { get; set; }
        public int CustomersWithOrdersCount { get; set; }
        public decimal AverageOrdersPerCustomer { get; set; }
        public IReadOnlyList<TopCustomerResponse> TopCustomers { get; set; } = [];
    }

    public sealed class TopCustomerResponse
    {
        public Guid CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int OrderCount { get; set; }
        public decimal TotalSpent { get; set; }
    }

    public sealed class ReturnReportResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Currency { get; set; } = CurrencyCatalog.BaseCurrency;
        public int TotalOrderCount { get; set; }
        public int ReturnRequestCount { get; set; }
        public decimal ReturnRatePercent { get; set; }
        public decimal RefundAmount { get; set; }
        public IReadOnlyList<ReturnReasonResponse> CommonReasons { get; set; } = [];
    }

    public sealed class ReturnReasonResponse
    {
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public sealed class RevenueDailyAggregate
    {
        public DateTime OccurredOn { get; set; }
        public decimal GrossRevenue { get; set; }
        public decimal RefundAmount { get; set; }
        public int OrderCount { get; set; }
    }

    public sealed class CustomerOrderReportMetrics
    {
        public int OrderCount { get; set; }
        public int CustomersWithOrdersCount { get; set; }
    }
}
