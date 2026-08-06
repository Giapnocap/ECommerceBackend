namespace ECommerceBackend.Application.DTOs
{
    public sealed class SalesSummaryQuery
    {
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public int LowStockThreshold { get; set; } = 10;
        public int TopProductLimit { get; set; } = 10;
    }

    public sealed class StatusBreakdownResponse
    {
        public string Status { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal Amount { get; set; }
    }

    public sealed class TopSellingProductResponse
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public long QuantitySold { get; set; }
        public decimal Revenue { get; set; }
    }

    public sealed class SalesSummaryResponse
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public int TotalOrders { get; set; }
        public int DeliveredOrders { get; set; }
        public int CancelledOrders { get; set; }
        public decimal GrossPaidAmount { get; set; }
        public decimal RefundedAmount { get; set; }
        public decimal NetRevenue { get; set; }
        public decimal PaidRevenue { get; set; }
        public decimal PendingPaymentAmount { get; set; }
        public int LowStockThreshold { get; set; }
        public int LowStockProductCount { get; set; }
        public IEnumerable<StatusBreakdownResponse> OrdersByStatus { get; set; }
            = Enumerable.Empty<StatusBreakdownResponse>();
        public IEnumerable<StatusBreakdownResponse> PaymentsByStatus { get; set; }
            = Enumerable.Empty<StatusBreakdownResponse>();
        public IEnumerable<TopSellingProductResponse> TopSellingProducts { get; set; }
            = Enumerable.Empty<TopSellingProductResponse>();
    }
}
