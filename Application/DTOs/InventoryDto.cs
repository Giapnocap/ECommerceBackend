namespace ECommerceBackend.Application.DTOs
{
    public sealed class InventoryTransactionResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public Guid? OrderId { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public string Type { get; set; } = string.Empty;
        public int QuantityChange { get; set; }
        public int BalanceAfter { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class LowStockProductResponse
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
    }

    public class InventoryQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class LowStockQueryParams : InventoryQueryParams
    {
        public int Threshold { get; set; } = 10;
    }
}
