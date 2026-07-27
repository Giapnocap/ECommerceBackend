using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Application.Validation;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public class ReportServiceTests
{
    [Fact]
    public async Task GetSalesSummaryAsync_AggregatesDeliveredSnapshotsAndStatusBreakdowns()
    {
        await using var context = TestAppDbContext.Create();
        var now = DateTime.UtcNow;
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Category",
            NormalizedName = "CATEGORY"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = "Current product name",
            Description = "Description",
            Price = 25m,
            StockQuantity = 10,
            CreatedAt = now.AddDays(-10)
        };
        var user = CreateUser("report_customer");
        var firstDelivered = CreateOrder(user.Id, now.AddHours(-2), OrderStatus.Delivered, 50m);
        var latestDelivered = CreateOrder(user.Id, now.AddHours(-1), OrderStatus.Delivered, 75m);
        var deliveredAfterDelay = CreateOrder(user.Id, now.AddDays(-5), OrderStatus.Delivered, 100m);
        var pending = CreateOrder(user.Id, now.AddMinutes(-30), OrderStatus.Pending, 2_500m);

        context.AddRange(category, product, user, firstDelivered, latestDelivered, deliveredAfterDelay, pending);
        context.OrderDetails.AddRange(
            CreateDetail(firstDelivered.Id, product.Id, "Original product", 2, 25m),
            CreateDetail(latestDelivered.Id, product.Id, "Renamed snapshot", 3, 25m),
            CreateDetail(deliveredAfterDelay.Id, product.Id, "Later delivery snapshot", 4, 25m),
            CreateDetail(pending.Id, product.Id, "Pending snapshot", 100, 25m));
        context.Payments.AddRange(
            CreatePayment(firstDelivered, PaymentStatus.Paid, firstDelivered.OrderDate.AddMinutes(10)),
            CreatePayment(latestDelivered, PaymentStatus.Paid, latestDelivered.OrderDate.AddMinutes(10)),
            CreatePayment(pending, PaymentStatus.Pending));
        context.OrderStatusHistories.AddRange(
            CreateStatusHistory(firstDelivered.Id, OrderStatus.Delivered, firstDelivered.OrderDate),
            CreateStatusHistory(latestDelivered.Id, OrderStatus.Delivered, latestDelivered.OrderDate),
            CreateStatusHistory(deliveredAfterDelay.Id, OrderStatus.Delivered, now.AddMinutes(-30)));
        await context.SaveChangesAsync();

        var result = await new ReportService(
            new ReportReadRepository(context)).GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = now.AddDays(-1),
                To = now.AddDays(1),
                LowStockThreshold = 10,
                TopProductLimit = 10
            });

        Assert.Equal(3, result.TotalOrders);
        Assert.Equal(3, result.DeliveredOrders);
        Assert.Equal(0, result.CancelledOrders);
        Assert.Equal(125m, result.GrossPaidAmount);
        Assert.Equal(0m, result.RefundedAmount);
        Assert.Equal(125m, result.NetRevenue);
        Assert.Equal(result.NetRevenue, result.PaidRevenue);
        Assert.Equal(2_500m, result.PendingPaymentAmount);
        Assert.Equal(1, result.LowStockProductCount);
        Assert.Equal(7, result.OrdersByStatus.Count());
        Assert.Equal(2, result.OrdersByStatus.Single(item => item.Status == nameof(OrderStatus.Delivered)).Count);
        Assert.Equal(1, result.OrdersByStatus.Single(item => item.Status == nameof(OrderStatus.Pending)).Count);
        Assert.Equal(2, result.PaymentsByStatus.Single(item => item.Status == nameof(PaymentStatus.Paid)).Count);
        Assert.Equal(1, result.PaymentsByStatus.Single(item => item.Status == nameof(PaymentStatus.Pending)).Count);

        var topProduct = Assert.Single(result.TopSellingProducts);
        Assert.Equal(product.Id, topProduct.ProductId);
        Assert.Equal("Later delivery snapshot", topProduct.ProductName);
        Assert.Equal(9, topProduct.QuantitySold);
        Assert.Equal(225m, topProduct.Revenue);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_UsesPaymentAndRefundOccurrenceTimesForCashFlow()
    {
        await using var context = TestAppDbContext.Create();
        var from = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        var user = CreateUser("cash_flow_customer");
        var paidInRange = CreateOrder(user.Id, from.AddDays(-2), OrderStatus.Delivered, 100m);
        var createdInRangePaidLater = CreateOrder(user.Id, from.AddHours(1), OrderStatus.Delivered, 200m);
        var refundedInRange = CreateOrder(user.Id, from.AddDays(-3), OrderStatus.Delivered, 50m);
        var firstPayment = CreatePayment(paidInRange, PaymentStatus.Paid, from.AddHours(2));
        var secondPayment = CreatePayment(createdInRangePaidLater, PaymentStatus.Paid, to.AddHours(1));
        var refundedPayment = CreatePayment(refundedInRange, PaymentStatus.Refunded, from.AddDays(-1));

        context.AddRange(user, paidInRange, createdInRangePaidLater, refundedInRange);
        context.Payments.AddRange(firstPayment, secondPayment, refundedPayment);
        context.PaymentStatusHistories.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = refundedPayment.Id,
            FromStatus = PaymentStatus.Paid,
            ToStatus = PaymentStatus.Refunded,
            Source = PaymentStatusChangeSource.Webhook,
            Reference = "evt-refund",
            OccurredAt = from.AddHours(3),
            CreatedAt = from.AddHours(3)
        });
        await context.SaveChangesAsync();

        var result = await new ReportService(
            new ReportReadRepository(context)).GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = from,
                To = to
            });

        Assert.Equal(1, result.TotalOrders);
        Assert.Equal(100m, result.GrossPaidAmount);
        Assert.Equal(50m, result.RefundedAmount);
        Assert.Equal(50m, result.NetRevenue);
        Assert.Equal(1, result.PaymentsByStatus.Single(item => item.Status == nameof(PaymentStatus.Paid)).Count);
        Assert.Equal(200m, result.PaymentsByStatus.Single(item => item.Status == nameof(PaymentStatus.Paid)).Amount);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_RejectsUnsafeReadBoundsOutsideHttpValidation()
    {
        await using var context = TestAppDbContext.Create();
        var service = new ReportService(new ReportReadRepository(context));
        var to = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);

        var invalidLimit = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = to.AddDays(-1),
                To = to,
                TopProductLimit = 0
            }));
        var invalidThreshold = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = to.AddDays(-1),
                To = to,
                LowStockThreshold = -1
            }));
        var invalidRange = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = to,
                To = to
            }));
        var excessiveRange = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetSalesSummaryAsync(new SalesSummaryQuery
            {
                From = to.AddDays(-367),
                To = to
            }));

        Assert.Equal("report_top_product_limit_invalid", invalidLimit.Code);
        Assert.Equal("report_low_stock_threshold_invalid", invalidThreshold.Code);
        Assert.Equal("report_range_invalid", invalidRange.Code);
        Assert.Equal("report_range_too_large", excessiveRange.Code);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_DefaultWindowUsesInjectedUtcClock()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 7, 20, 15, 45, 30, TimeSpan.Zero);
        var service = new ReportService(
            new ReportReadRepository(context),
            new FixedTimeProvider(now));

        var result = await service.GetSalesSummaryAsync(new SalesSummaryQuery());

        Assert.Equal(now.UtcDateTime.AddDays(-30), result.From);
        Assert.Equal(now.UtcDateTime, result.To);
        Assert.Equal(DateTimeKind.Utc, result.From.Kind);
        Assert.Equal(DateTimeKind.Utc, result.To.Kind);
        Assert.Equal(0, result.TotalOrders);
    }

    [Fact]
    public void SalesSummaryValidator_RejectsUnsafeReadBounds()
    {
        var validator = new SalesSummaryQueryValidator();

        var result = validator.Validate(new SalesSummaryQuery
        {
            TopProductLimit = 101,
            LowStockThreshold = -1
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SalesSummaryQuery.TopProductLimit));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(SalesSummaryQuery.LowStockThreshold));
    }

    private static User CreateUser(string userName)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FullName = "Report Customer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = DateTime.UtcNow
        };

    private static Order CreateOrder(
        Guid userId,
        DateTime orderDate,
        OrderStatus status,
        decimal amount)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IdempotencyRequestHash = new string('A', 64),
            OrderDate = orderDate,
            ShippingAddress = "Address"
        };
        order.SetPricing(amount, discount: 0, shipping: 0, tax: 0);

        if (status is OrderStatus.Confirmed or OrderStatus.Shipping or OrderStatus.Delivered)
            order.ChangeStatus(OrderStatus.Confirmed, null);
        if (status is OrderStatus.Shipping or OrderStatus.Delivered)
            order.ChangeStatus(OrderStatus.Shipping, null);
        if (status == OrderStatus.Delivered)
            order.ChangeStatus(OrderStatus.Delivered, null);
        if (status == OrderStatus.Cancelled)
            order.ChangeStatus(OrderStatus.Cancelled, null);

        return order;
    }

    private static OrderDetail CreateDetail(
        Guid orderId,
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice)
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ProductId = productId,
            ProductNameSnapshot = productName,
            UnitPrice = unitPrice,
            Quantity = quantity
        };

    private static OrderStatusHistory CreateStatusHistory(
        Guid orderId,
        OrderStatus status,
        DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            ToStatus = status,
            CreatedAt = createdAt
        };

    private static Payment CreatePayment(
        Order order,
        PaymentStatus status,
        DateTime? paidAt = null)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = PaymentMethod.CashOnDelivery,
            Amount = order.TotalAmount,
            Provider = "cod",
            CreatedAt = order.OrderDate
        };
        var occurredAt = paidAt ?? order.OrderDate;

        if (status is PaymentStatus.Paid or PaymentStatus.Refunded)
            payment.ChangeStatus(PaymentStatus.Paid, occurredAt);
        if (status == PaymentStatus.Refunded)
            payment.ChangeStatus(PaymentStatus.Refunded, occurredAt);
        if (status == PaymentStatus.Failed)
            payment.ChangeStatus(PaymentStatus.Failed, occurredAt);
        if (status == PaymentStatus.Cancelled)
            payment.ChangeStatus(PaymentStatus.Cancelled, occurredAt);

        return payment;
    }
}
