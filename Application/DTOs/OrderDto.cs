using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.DTOs
{
    public class OrderDetailResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal SubTotal => UnitPrice * Quantity;
    }

    public class PaymentStatusHistoryResponse
    {
        public Guid Id { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string? Reference { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class PaymentResponse
    {
        public Guid Id { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string? Provider { get; set; }
        public string? ProviderTransactionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public IEnumerable<PaymentStatusHistoryResponse> StatusHistory { get; set; } = Enumerable.Empty<PaymentStatusHistoryResponse>();
    }

    public class OrderStatusHistoryResponse
    {
        public Guid Id { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public string? FromStatus { get; set; }
        public string ToStatus { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class OrderResponse
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal SubtotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
        public string? CancellationReason { get; set; }
        public PaymentResponse? Payment { get; set; }
        public IEnumerable<OrderDetailResponse> OrderDetails { get; set; } = Enumerable.Empty<OrderDetailResponse>();
        public IEnumerable<OrderStatusHistoryResponse> StatusHistory { get; set; } = Enumerable.Empty<OrderStatusHistoryResponse>();
    }

    public class PlaceOrderRequest
    {
        public string ShippingAddress { get; set; } = string.Empty;
        public string? Note { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;
    }

    public class UpdateOrderStatusRequest
    {
        public OrderStatus Status { get; set; }
        public string? Note { get; set; }
    }

    public class CancelOrderRequest
    {
        public string? Reason { get; set; }
    }

    public class OrderQueryParams
    {
        public OrderStatus? Status { get; set; }
        public Guid? UserId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
