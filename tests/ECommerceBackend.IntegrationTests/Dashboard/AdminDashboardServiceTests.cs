using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class AdminDashboardServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_AggregatesOperationalKpisUsingCurrentBusinessDefinitions()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var customerRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Customer);
        var existingCustomer = CreateUser("existing_customer", now.UtcDateTime.AddMonths(-1));
        var newCustomer = CreateUser("new_customer", now.UtcDateTime.AddDays(-2));
        var pendingOrder = CreateOrder(existingCustomer.Id, now.UtcDateTime.AddHours(-1), OrderStatus.Pending, 25m);
        var completedOrder = CreateOrder(existingCustomer.Id, now.UtcDateTime.AddDays(-2), OrderStatus.Delivered, 100m);
        var cancelledOrder = CreateOrder(newCustomer.Id, now.UtcDateTime.AddDays(-3), OrderStatus.Cancelled, 40m);
        var refundedOrder = CreateOrder(newCustomer.Id, now.UtcDateTime.AddDays(-4), OrderStatus.Delivered, 50m);
        var paidPayment = CreatePayment(
            completedOrder,
            PaymentStatus.Paid,
            now.UtcDateTime.AddHours(-2));
        var refundedPayment = CreatePayment(
            refundedOrder,
            PaymentStatus.Refunded,
            now.UtcDateTime.AddDays(-1));
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(),
            completedOrder.Id,
            existingCustomer.Id,
            "Sản phẩm bị lỗi.",
            now.UtcDateTime.AddHours(-1));
        var lowStockProduct = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = Guid.NewGuid(),
            Name = "Low stock product",
            Description = "Description",
            Price = 20m,
            StockQuantity = 5,
            CreatedAt = now.UtcDateTime.AddDays(-10)
        };

        context.AddRange(
            existingCustomer,
            newCustomer,
            pendingOrder,
            completedOrder,
            cancelledOrder,
            refundedOrder,
            paidPayment,
            refundedPayment,
            returnRequest,
            lowStockProduct);
        context.UserRoles.AddRange(
            UserRole.Create(existingCustomer.Id, customerRole),
            UserRole.Create(newCustomer.Id, customerRole));
        context.PaymentStatusHistories.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = refundedPayment.Id,
            Payment = refundedPayment,
            FromStatus = PaymentStatus.Paid,
            ToStatus = PaymentStatus.Refunded,
            Source = PaymentStatusChangeSource.ManualRefund,
            Reference = "refund-dashboard",
            OccurredAt = now.UtcDateTime.AddHours(-3),
            CreatedAt = now.UtcDateTime.AddHours(-3)
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context, new FixedTimeProvider(now)).GetSummaryAsync(
            new DashboardSummaryQuery { LowStockThreshold = 5 });

        Assert.Equal(now.UtcDateTime, result.GeneratedAt);
        Assert.Equal(50m, result.RevenueToday);
        Assert.Equal(100m, result.RevenueThisMonth);
        Assert.Equal(1, result.OrdersToday);
        Assert.Equal(4, result.TotalOrders);
        Assert.Equal(1, result.PendingOrderCount);
        Assert.Equal(2, result.CompletedOrderCount);
        Assert.Equal(1, result.CancelledOrderCount);
        Assert.Equal(1, result.OpenReturnRequestCount);
        Assert.Equal(2, result.TotalCustomerCount);
        Assert.Equal(1, result.NewCustomerCountThisMonth);
        Assert.Equal(5, result.LowStockThreshold);
        Assert.Equal(1, result.LowStockProductCount);
    }

    [Fact]
    public async Task GetRevenueAsync_GroupsDatabaseDailyAggregatesWithoutLoadingPayments()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var customer = CreateUser("revenue_customer", now.UtcDateTime.AddDays(-10));
        var firstOrder = CreateOrder(customer.Id, now.UtcDateTime.AddDays(-3), OrderStatus.Delivered, 100m);
        var secondOrder = CreateOrder(customer.Id, now.UtcDateTime.AddDays(-3), OrderStatus.Delivered, 40m);
        var firstPayment = CreatePayment(
            firstOrder,
            PaymentStatus.Paid,
            new DateTime(2026, 8, 17, 8, 0, 0, DateTimeKind.Utc));
        var secondPayment = CreatePayment(
            secondOrder,
            PaymentStatus.Refunded,
            new DateTime(2026, 8, 17, 9, 0, 0, DateTimeKind.Utc));

        context.AddRange(customer, firstOrder, secondOrder, firstPayment, secondPayment);
        context.PaymentStatusHistories.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = secondPayment.Id,
            Payment = secondPayment,
            FromStatus = PaymentStatus.Paid,
            ToStatus = PaymentStatus.Refunded,
            Source = PaymentStatusChangeSource.ManualRefund,
            Reference = "refund-revenue",
            OccurredAt = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 8, 18, 9, 0, 0, DateTimeKind.Utc)
        });
        await context.SaveChangesAsync();

        var result = await CreateService(context, new FixedTimeProvider(now)).GetRevenueAsync(new DashboardRevenueQuery
        {
            From = new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
            GroupBy = "day"
        });

        Assert.Equal("day", result.GroupBy);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc), result.Items[0].PeriodStart);
        Assert.Equal(140m, result.Items[0].GrossRevenue);
        Assert.Equal(0m, result.Items[0].RefundedAmount);
        Assert.Equal(140m, result.Items[0].NetRevenue);
        Assert.Equal(new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc), result.Items[1].PeriodStart);
        Assert.Equal(0m, result.Items[1].GrossRevenue);
        Assert.Equal(40m, result.Items[1].RefundedAmount);
        Assert.Equal(-40m, result.Items[1].NetRevenue);
    }

    [Fact]
    public async Task GetRecentActivitiesAsync_ReturnsBoundedNewestEventsAcrossSources()
    {
        await using var context = TestAppDbContext.Create();
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        var customer = CreateUser("activity_customer", now.UtcDateTime.AddDays(-10));
        var order = CreateOrder(customer.Id, now.UtcDateTime.AddHours(-3), OrderStatus.Pending, 25m);
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(),
            order.Id,
            customer.Id,
            "Không còn nhu cầu.",
            now.UtcDateTime.AddHours(-2));
        var auditEvent = new AuditEvent
        {
            Id = Guid.NewGuid(),
            ActorUserId = customer.Id,
            Action = "inventory.adjust",
            EntityType = "Product",
            EntityId = Guid.NewGuid().ToString(),
            CorrelationId = "dashboard-test",
            CreatedAt = now.UtcDateTime.AddHours(-1)
        };

        context.AddRange(customer, order, returnRequest, auditEvent);
        await context.SaveChangesAsync();

        var result = await CreateService(context, new FixedTimeProvider(now)).GetRecentActivitiesAsync(
            new DashboardRecentActivitiesQuery { Limit = 2 });

        Assert.Equal(2, result.Count);
        Assert.Equal("administrative_action", result[0].Type);
        Assert.Equal("return_request", result[1].Type);
        Assert.DoesNotContain(result, activity => activity.Action == "order.created");
    }

    private static AdminDashboardService CreateService(
        ECommerceBackend.Infrastructure.Data.AppDbContext context,
        TimeProvider timeProvider)
        => new(
            new AdminDashboardReadRepository(context),
            new ReportReadRepository(context),
            new InventoryService(new InventoryRepository(context)),
            timeProvider);

    private static User CreateUser(string userName, DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            FullName = "Dashboard Customer",
            CreatedAt = createdAt
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

    private static Payment CreatePayment(
        Order order,
        PaymentStatus status,
        DateTime paidAt)
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

        payment.ChangeStatus(PaymentStatus.Paid, paidAt);
        if (status == PaymentStatus.Refunded)
            payment.ChangeStatus(PaymentStatus.Refunded, paidAt.AddMinutes(1));

        return payment;
    }
}
