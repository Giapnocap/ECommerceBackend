using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Mappings;

public static class ResponseMappings
{
    public static UserResponse ToResponse(this User user)
        => new()
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            CreatedAt = user.CreatedAt,
            Roles = user.UserRoles
                .Where(userRole => userRole.Role != null)
                .Select(userRole => userRole.Role!.Name)
                .ToArray()
        };

    public static CategoryResponse ToResponse(this Category category)
        => new()
        {
            Id = category.Id,
            Name = category.Name,
            ParentId = category.ParentId,
            ParentName = category.Parent?.Name,
            Children = category.Children
                .Where(child => !child.IsDeleted)
                .Select(ToResponse)
                .ToArray()
        };

    public static ProductImageResponse ToProductImageResponse(this ProductImage image)
        => new()
        {
            Id = image.Id,
            ImageUrl = image.ImageUrl,
            IsMain = image.IsMain
        };

    public static UploadImageResponse ToUploadImageResponse(this ProductImage image)
        => new()
        {
            Id = image.Id,
            ImageUrl = image.ImageUrl,
            IsMain = image.IsMain
        };

    public static ProductResponse ToResponse(this Product product)
        => new()
        {
            Id = product.Id,
            Version = Convert.ToBase64String(product.RowVersion),
            Name = product.Name,
            Price = product.Price,
            StockQuantity = product.StockQuantity,
            LowStockThreshold = product.LowStockThreshold,
            Description = product.Description,
            CategoryId = product.CategoryId,
            CategoryName = product.Category?.Name ?? string.Empty,
            CreatedAt = product.CreatedAt,
            Images = product.Images
                .Select(ToProductImageResponse)
                .ToArray()
        };

    public static CartResponse ToResponse(this Cart cart)
        => new()
        {
            Id = cart.Id,
            Items = cart.CartItems
                .Select(ToResponse)
                .ToArray()
        };

    public static CartItemResponse ToResponse(this CartItem item)
    {
        var product = item.Product;
        var isActive = product is { IsDeleted: false };
        return new CartItemResponse
        {
            Id = item.Id,
            ProductId = item.ProductId,
            ProductName = product?.Name ?? string.Empty,
            ProductImageUrl = product?.Images
                .OrderByDescending(image => image.IsMain)
                .Select(image => image.ImageUrl)
                .FirstOrDefault(),
            UnitPrice = isActive ? product!.Price : item.UnitPrice,
            Quantity = item.Quantity,
            IsAvailable = isActive && product!.StockQuantity >= item.Quantity,
            AvailableStock = isActive ? product!.StockQuantity : 0
        };
    }

    public static OrderResponse ToResponse(this Order order)
        => new()
        {
            Id = order.Id,
            UserId = order.UserId,
            OrderNumber = order.OrderNumber,
            OrderDate = order.OrderDate,
            SubtotalAmount = order.SubtotalAmount,
            DiscountAmount = order.DiscountAmount,
            ShippingFee = order.ShippingFee,
            TaxAmount = order.TaxAmount,
            TotalAmount = order.TotalAmount,
            Currency = order.Currency,
            BaseSubtotalAmount = order.BaseSubtotalAmount,
            BaseDiscountAmount = order.BaseDiscountAmount,
            BaseShippingFee = order.BaseShippingFee,
            BaseTaxAmount = order.BaseTaxAmount,
            BaseTotalAmount = order.BaseTotalAmount,
            BaseCurrency = order.BaseCurrency,
            ExchangeRate = order.ExchangeRate,
            ExchangeRateCapturedAt = order.ExchangeRateCapturedAt,
            ShippingMethod = order.ShippingMethod.ToString(),
            PromotionCode = order.PromotionCodeSnapshot,
            Status = order.Status.ToString(),
            ShippingAddress = order.ShippingAddress,
            RecipientName = order.RecipientName,
            RecipientPhone = order.RecipientPhone,
            Note = order.Note,
            ExpiresAt = order.ExpiresAt,
            CancelledAt = order.CancelledAt,
            ExpiredAt = order.ExpiredAt,
            CancellationReason = order.CancellationReason,
            Payment = order.Payment?.ToResponse(),
            Shipment = order.Shipment?.ToResponse(),
            ReturnRequest = order.ReturnRequest?.ToResponse(),
            OrderDetails = order.OrderDetails.Select(ToResponse).ToArray(),
            StatusHistory = order.StatusHistory.Select(ToResponse).ToArray()
        };

    public static ShipmentResponse ToResponse(this Shipment shipment)
        => new()
        {
            Id = shipment.Id,
            Carrier = shipment.Carrier,
            TrackingNumber = shipment.TrackingNumber,
            ShippedAt = shipment.ShippedAt,
            DeliveredAt = shipment.DeliveredAt
        };

    public static ReturnRequestResponse ToResponse(
        this ReturnRequest returnRequest)
        => new()
        {
            Id = returnRequest.Id,
            Reason = returnRequest.Reason,
            Status = returnRequest.Status.ToString(),
            RequestedAt = returnRequest.RequestedAt,
            ReviewedAt = returnRequest.ReviewedAt,
            ReviewNote = returnRequest.ReviewNote,
            ReceivedAt = returnRequest.ReceivedAt,
            InspectionNote = returnRequest.InspectionNote,
            RefundedAt = returnRequest.RefundedAt
        };

    public static PromotionResponse ToResponse(this Promotion promotion)
        => new()
        {
            Id = promotion.Id,
            Code = promotion.Code,
            Type = promotion.Type.ToString(),
            Value = promotion.Value,
            MinimumSubtotal = promotion.MinimumSubtotal,
            MaximumDiscountAmount = promotion.MaximumDiscountAmount,
            StartsAt = promotion.StartsAt,
            EndsAt = promotion.EndsAt,
            UsageLimit = promotion.UsageLimit,
            UsageLimitPerCustomer = promotion.UsageLimitPerCustomer,
            UsedCount = promotion.UsedCount,
            IsActive = promotion.IsActive,
            CreatedAt = promotion.CreatedAt,
            UpdatedAt = promotion.UpdatedAt
        };

    public static OrderDetailResponse ToResponse(this OrderDetail detail)
        => new()
        {
            Id = detail.Id,
            ProductId = detail.ProductId,
            ProductName = detail.ProductNameSnapshot,
            Quantity = detail.Quantity,
            UnitPrice = detail.UnitPrice,
            BaseUnitPrice = detail.BaseUnitPrice
        };

    public static PaymentResponse ToResponse(this Payment payment)
        => new()
        {
            Id = payment.Id,
            Method = payment.Method.ToString(),
            Status = payment.Status.ToString(),
            Amount = payment.Amount,
            Currency = payment.Currency,
            RefundedAmount = payment.RefundedAmount,
            Provider = payment.Provider,
            ProviderTransactionId = payment.ProviderTransactionId,
            ExternalCreatedAt = payment.ExternalCreatedAt,
            CreatedAt = payment.CreatedAt,
            PaidAt = payment.PaidAt,
            StatusHistory = payment.StatusHistory.Select(ToResponse).ToArray()
        };

    public static PaymentStatusHistoryResponse ToResponse(this PaymentStatusHistory history)
        => new()
        {
            Id = history.Id,
            ChangedByUserId = history.ChangedByUserId,
            FromStatus = history.FromStatus?.ToString(),
            ToStatus = history.ToStatus.ToString(),
            Source = history.Source.ToString(),
            Reference = history.Reference,
            OccurredAt = history.OccurredAt,
            CreatedAt = history.CreatedAt
        };

    public static OrderStatusHistoryResponse ToResponse(this OrderStatusHistory history)
        => new()
        {
            Id = history.Id,
            ChangedByUserId = history.ChangedByUserId,
            FromStatus = history.FromStatus?.ToString(),
            ToStatus = history.ToStatus.ToString(),
            Note = history.Note,
            CreatedAt = history.CreatedAt
        };
}
