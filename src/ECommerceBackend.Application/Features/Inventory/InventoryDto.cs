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
        public int BeforeQuantity { get; set; }
        public int QuantityChange { get; set; }
        public int BalanceAfter { get; set; }
        public string? Reference { get; set; }
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

    public sealed class AdjustProductStockRequest
    {
        public int TargetQuantity { get; set; }
        public string? Reference { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class StockInRequest
    {
        public int Quantity { get; set; }
        public string? Reference { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class UpdateLowStockThresholdRequest
    {
        public int Threshold { get; set; }
    }

    public class InventoryQueryParams
    {
        public string? Type { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }
        public Guid? ActorUserId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class InventoryProductQueryParams
    {
        public string? Keyword { get; set; }
        public Guid? CategoryId { get; set; }
        public bool LowStockOnly { get; set; }
        public int? LowStockThreshold { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class InventoryProductResponse
    {
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public int StockQuantity { get; set; }
        public int LowStockThreshold { get; set; }
        public bool IsLowStock { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public sealed class LowStockQueryParams : InventoryQueryParams
    {
        public int Threshold { get; set; } = 10;
    }
}
