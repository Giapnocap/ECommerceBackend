using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void OrderStatus_ValidTransitionMutatesOnceAndSameStatusIsIdempotent()
    {
        var order = new Order();

        var changed = order.ChangeStatus(OrderStatus.Confirmed, null);
        var unchanged = order.ChangeStatus(OrderStatus.Confirmed, null);

        Assert.True(changed.Changed);
        Assert.Equal(OrderStatus.Pending, changed.Previous);
        Assert.Equal(OrderStatus.Confirmed, changed.Current);
        Assert.False(unchanged.Changed);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void OrderStatus_InvalidTransitionDoesNotMutateAggregate()
    {
        var order = new Order();

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.ChangeStatus(OrderStatus.Shipping, null));

        Assert.Equal("order_status_transition_invalid", exception.Code);
        Assert.Equal(OrderStatus.Pending, order.Status);
    }

    [Fact]
    public void OrderStatus_PaidOrderCannotBeCancelled()
    {
        var order = new Order();
        order.ChangeStatus(OrderStatus.Confirmed, PaymentStatus.Paid);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.ChangeStatus(OrderStatus.Cancelled, PaymentStatus.Paid));

        Assert.Equal("order_paid_cancellation_forbidden", exception.Code);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
    }

    [Fact]
    public void OrderPricing_ComputesConsistentBreakdown()
    {
        var order = new Order();

        order.SetPricing(
            subtotal: 100m,
            discount: 10m,
            shipping: 5m,
            tax: 8m);

        Assert.Equal(100m, order.SubtotalAmount);
        Assert.Equal(10m, order.DiscountAmount);
        Assert.Equal(5m, order.ShippingFee);
        Assert.Equal(8m, order.TaxAmount);
        Assert.Equal(103m, order.TotalAmount);
    }

    [Fact]
    public void OrderPricing_InvalidUpdateLeavesPreviousBreakdownUntouched()
    {
        var order = new Order();
        order.SetPricing(100m, discount: 0, shipping: 0, tax: 0);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.SetPricing(100m, discount: 101m, shipping: 0, tax: 0));

        Assert.Equal("order_discount_invalid", exception.Code);
        Assert.Equal(100m, order.SubtotalAmount);
        Assert.Equal(100m, order.TotalAmount);
    }

    [Fact]
    public void OrderPricing_SubtotalRejectsUnsupportedScaleAndEmptyOrder()
    {
        var scaleException = Assert.Throws<DomainRuleViolationException>(() =>
            OrderPricingPolicy.CalculateSubtotal(
            [
                new OrderPricingLine("Product", 10.001m, 1)
            ]));
        var emptyException = Assert.Throws<DomainRuleViolationException>(() =>
            OrderPricingPolicy.CalculateSubtotal([]));

        Assert.Equal("order_unit_price_invalid", scaleException.Code);
        Assert.Equal("order_empty", emptyException.Code);
    }

    [Fact]
    public void PaymentStatus_PaidThenRefundedPreservesPaidTimestamp()
    {
        var paidAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var payment = new Payment { CreatedAt = paidAt.AddMinutes(-1) };

        var paid = payment.ChangeStatus(PaymentStatus.Paid, paidAt);
        var refunded = payment.ChangeStatus(PaymentStatus.Refunded, paidAt.AddHours(1));

        Assert.True(paid.Changed);
        Assert.True(refunded.Changed);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(paidAt, payment.PaidAt);
    }

    [Fact]
    public void PaymentStatus_InvalidTransitionDoesNotMutateAggregate()
    {
        var payment = new Payment { CreatedAt = DateTime.UtcNow.AddMinutes(-1) };

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            payment.ChangeStatus(PaymentStatus.Refunded, DateTime.UtcNow));

        Assert.Equal("payment_status_transition_invalid", exception.Code);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.PaidAt);
    }

    [Fact]
    public void PaymentStatus_OccurrenceBeforeCreationDoesNotMutateAggregate()
    {
        var createdAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var payment = new Payment { CreatedAt = createdAt };

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            payment.ChangeStatus(PaymentStatus.Paid, createdAt.AddTicks(-1)));

        Assert.Equal("payment_occurrence_before_creation", exception.Code);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.PaidAt);
    }

    [Fact]
    public void PaymentStatus_RefundBeforePaidDoesNotMutateAggregate()
    {
        var paidAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var payment = new Payment { CreatedAt = paidAt.AddMinutes(-5) };
        payment.ChangeStatus(PaymentStatus.Paid, paidAt);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            payment.ChangeStatus(PaymentStatus.Refunded, paidAt.AddTicks(-1)));

        Assert.Equal("payment_refund_before_paid", exception.Code);
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(paidAt, payment.PaidAt);
    }

    [Fact]
    public void InventoryPolicy_ReserveAndReleaseProduceAuditableMutation()
    {
        var product = new Product
        {
            Name = "Product",
            StockQuantity = 5
        };

        var reserved = InventoryPolicy.Reserve(product, 3);
        var released = InventoryPolicy.Release(product, 2);

        Assert.Equal(new InventoryMutation(-3, 2), reserved);
        Assert.Equal(new InventoryMutation(2, 4), released);
        Assert.Equal(4, product.StockQuantity);
    }

    [Fact]
    public void InventoryPolicy_InsufficientStockDoesNotMutateProduct()
    {
        var product = new Product
        {
            Name = "Product",
            StockQuantity = 2
        };

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            InventoryPolicy.Reserve(product, 3));

        Assert.Equal("inventory_insufficient", exception.Code);
        Assert.Equal(2, product.StockQuantity);
    }

    [Fact]
    public void InventoryPolicy_AdjustToReturnsLedgerConsistentMutation()
    {
        var product = new Product { Name = "Product", StockQuantity = 7 };

        var mutation = InventoryPolicy.AdjustTo(product, 3);

        Assert.Equal(new InventoryMutation(-4, 3), mutation);
        Assert.Equal(3, product.StockQuantity);
    }

    [Fact]
    public void DomainRuleGuard_PreservesStableBusinessErrorCode()
    {
        var exception = Assert.Throws<BusinessException>(() =>
            DomainRuleGuard.AsBusiness(() =>
                InventoryPolicy.Reserve(
                    new Product { Name = "Unavailable", IsDeleted = true },
                    1)));

        Assert.Equal("inventory_product_unavailable", exception.Code);
        Assert.IsType<DomainRuleViolationException>(exception.InnerException);
    }

    [Fact]
    public void DomainRuleGuard_PreservesStableConflictErrorCode()
    {
        var occurredAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var payment = new Payment { CreatedAt = occurredAt.AddMinutes(-1) };
        var exception = Assert.Throws<ConflictException>(() =>
            DomainRuleGuard.AsConflict(() =>
                payment.ChangeStatus(PaymentStatus.Refunded, occurredAt)));

        Assert.Equal("payment_status_transition_invalid", exception.Code);
        Assert.Equal(409, exception.StatusCode);
        Assert.IsType<DomainRuleViolationException>(exception.InnerException);
    }

    [Fact]
    public void RefreshToken_RotationCapturesReplacementAndTimestamp()
    {
        var createdAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var token = new RefreshToken
        {
            TokenHash = "CURRENT",
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddDays(7)
        };

        token.Rotate(createdAt.AddMinutes(5), "REPLACEMENT");

        Assert.True(token.IsRevoked);
        Assert.Equal(createdAt.AddMinutes(5), token.RevokedAt);
        Assert.Equal("Rotated", token.RevocationReason);
        Assert.Equal("REPLACEMENT", token.ReplacedByTokenHash);
        Assert.False(token.IsActiveAt(createdAt.AddMinutes(5)));
    }

    [Fact]
    public void RefreshToken_ExpiredTokenCannotRotateAndRemainsUnchanged()
    {
        var createdAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var token = new RefreshToken
        {
            TokenHash = "CURRENT",
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddMinutes(1)
        };

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            token.Rotate(createdAt.AddMinutes(2), "REPLACEMENT"));

        Assert.Equal("refresh_token_not_active", exception.Code);
        Assert.False(token.IsRevoked);
        Assert.Null(token.ReplacedByTokenHash);
    }

    [Fact]
    public void User_SecurityChangesIncrementTokenVersionThroughDomainMethods()
    {
        var occurredAt = new DateTime(2026, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        var user = new User { PasswordHash = "old-hash" };

        user.ChangePasswordHash("new-hash", occurredAt);
        user.InvalidateSessions();

        Assert.Equal("new-hash", user.PasswordHash);
        Assert.Equal(occurredAt, user.PasswordChangedAt);
        Assert.Equal(2, user.TokenVersion);
    }

    [Fact]
    public void AggregateInvariantSetters_AreNotPublic()
    {
        Assert.False(typeof(Order).GetProperty(nameof(Order.Status))!.SetMethod!.IsPublic);
        Assert.False(typeof(Order).GetProperty(nameof(Order.TotalAmount))!.SetMethod!.IsPublic);
        Assert.False(typeof(Payment).GetProperty(nameof(Payment.Status))!.SetMethod!.IsPublic);
        Assert.False(typeof(Payment).GetProperty(nameof(Payment.PaidAt))!.SetMethod!.IsPublic);
        Assert.False(typeof(RefreshToken).GetProperty(nameof(RefreshToken.RevokedAt))!.SetMethod!.IsPublic);
        Assert.False(typeof(User).GetProperty(nameof(User.TokenVersion))!.SetMethod!.IsPublic);
    }
}
