using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Application.DTOs
{
    public sealed class CustomerQueryParams
    {
        public string? Keyword { get; set; }
        public string? Status { get; set; }
        public DateTime? RegisteredFrom { get; set; }
        public DateTime? RegisteredTo { get; set; }
        public string? SortBy { get; set; }
        public string? SortOrder { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class CustomerListItemResponse
    {
        public Guid CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
        public DateTime RegisteredAt { get; set; }
        public int OrderCount { get; set; }
        public decimal TotalSpent { get; set; }
        public string SpendingCurrency { get; set; } = CurrencyCatalog.BaseCurrency;
    }

    public sealed class CustomerDetailResponse
    {
        public Guid CustomerId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string AccountStatus { get; set; } = string.Empty;
        public DateTime? LockedUntil { get; set; }
        public DateTime RegisteredAt { get; set; }
        public int TotalOrderCount { get; set; }
        public int CompletedOrderCount { get; set; }
        public int CancelledOrderCount { get; set; }
        public int ReturnRequestCount { get; set; }
        public decimal TotalSpent { get; set; }
        public string SpendingCurrency { get; set; } = CurrencyCatalog.BaseCurrency;
        public CustomerLastOrderResponse? LastOrder { get; set; }
    }

    public sealed class CustomerLastOrderResponse
    {
        public Guid OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime OrderedAt { get; set; }
    }

    public sealed class CustomerOrderResponse
    {
        public Guid OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime OrderedAt { get; set; }
    }

    public sealed class CustomerReturnResponse
    {
        public Guid ReturnRequestId { get; set; }
        public Guid OrderId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public DateTime? RefundedAt { get; set; }
    }

    public sealed class CustomerPageQueryParams
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public sealed class CustomerAccountStatusResponse
    {
        public Guid CustomerId { get; set; }
        public string AccountStatus { get; set; } = string.Empty;
        public DateTime? LockedUntil { get; set; }
        public bool Changed { get; set; }
    }
}
