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
        public decimal BaseUnitPrice { get; set; }
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
        public string Currency { get; set; } = "VND";
        public decimal RefundedAmount { get; set; }
        public string? Provider { get; set; }
        public string? ProviderTransactionId { get; set; }
        public DateTime? ExternalCreatedAt { get; set; }
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

    public sealed class ShipmentResponse
    {
        public Guid Id { get; set; }
        public string Carrier { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public DateTime ShippedAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }

    public sealed class ReturnRequestResponse
    {
        public Guid Id { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public string? ReviewNote { get; set; }
        public DateTime? ReceivedAt { get; set; }
        public string? InspectionNote { get; set; }
        public DateTime? RefundedAt { get; set; }
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
        public string Currency { get; set; } = "VND";
        public decimal BaseSubtotalAmount { get; set; }
        public decimal BaseDiscountAmount { get; set; }
        public decimal BaseShippingFee { get; set; }
        public decimal BaseTaxAmount { get; set; }
        public decimal BaseTotalAmount { get; set; }
        public string BaseCurrency { get; set; } = "VND";
        public decimal ExchangeRate { get; set; }
        public DateTime ExchangeRateCapturedAt { get; set; }
        public string ShippingMethod { get; set; } = string.Empty;
        public string? PromotionCode { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ShippingAddress { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public string? RecipientPhone { get; set; }
        public string? Note { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? ExpiredAt { get; set; }
        public string? CancellationReason { get; set; }
        public PaymentResponse? Payment { get; set; }
        public ShipmentResponse? Shipment { get; set; }
        public ReturnRequestResponse? ReturnRequest { get; set; }
        public IEnumerable<OrderDetailResponse> OrderDetails { get; set; } = Enumerable.Empty<OrderDetailResponse>();
        public IEnumerable<OrderStatusHistoryResponse> StatusHistory { get; set; } = Enumerable.Empty<OrderStatusHistoryResponse>();
    }

    public sealed class OrderSummaryResponse
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "VND";
        public string ShippingMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public int TotalItemQuantity { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentStatus { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public class PlaceOrderRequest
    {
        public string ShippingAddress { get; set; } = string.Empty;
        public string? RecipientName { get; set; }
        public string? RecipientPhone { get; set; }
        public string? Note { get; set; }
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnDelivery;
        public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.Standard;
        public string? PromotionCode { get; set; }
        public decimal? ExpectedTotalAmount { get; set; }
        public string? Currency { get; set; }
    }

    public sealed class OrderQuoteRequest
    {
        public ShippingMethod ShippingMethod { get; set; } = ShippingMethod.Standard;
        public string? PromotionCode { get; set; }
        public string? Currency { get; set; }
    }

    public sealed class OrderQuoteResponse
    {
        public decimal SubtotalAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal ShippingFee { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal BaseSubtotalAmount { get; set; }
        public decimal BaseDiscountAmount { get; set; }
        public decimal BaseShippingFee { get; set; }
        public decimal BaseTaxAmount { get; set; }
        public decimal BaseTotalAmount { get; set; }
        public string BaseCurrency { get; set; } = "VND";
        public decimal ExchangeRate { get; set; }
        public DateTime ExchangeRateCapturedAt { get; set; }
        public string ShippingMethod { get; set; } = string.Empty;
        public string? PromotionCode { get; set; }
        public DateTime CalculatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
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

    public sealed class RecordOrderRefundRequest
    {
        public string Reference { get; set; } = string.Empty;
        public decimal? Amount { get; set; }
        public string? Note { get; set; }
    }

    public sealed class DispatchShipmentRequest
    {
        public string Carrier { get; set; } = string.Empty;
        public string TrackingNumber { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    public sealed class MarkShipmentDeliveredRequest
    {
        public string? Note { get; set; }
    }

    public sealed class CreateReturnRequest
    {
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class ReviewReturnRequest
    {
        public ReturnReviewDecision Decision { get; set; }
        public string? Note { get; set; }
    }

    public sealed class ReceiveReturnRequest
    {
        public string InspectionNote { get; set; } = string.Empty;
    }

    public class OrderQueryParams
    {
        public OrderStatus? Status { get; set; }
        public Guid? UserId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }
}
