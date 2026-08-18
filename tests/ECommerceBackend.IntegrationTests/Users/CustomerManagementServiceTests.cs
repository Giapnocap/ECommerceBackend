using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class CustomerManagementServiceTests
{
    [Fact]
    public async Task GetCustomersAsync_ProjectsCustomerOnlyOperationalSummaries()
    {
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var customerRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Customer);
        var staffRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Staff);
        var activeCustomer = CreateUser("active_customer", now.UtcDateTime.AddDays(-10));
        var lockedCustomer = CreateUser("locked_customer", now.UtcDateTime.AddDays(-5));
        var staff = CreateUser("staff_account", now.UtcDateTime.AddDays(-2));
        lockedCustomer.LockByAdministrator();

        var activeOrder = CreateOrder(
            activeCustomer.Id,
            now.UtcDateTime.AddDays(-1),
            OrderStatus.Delivered,
            100m);
        var lockedOrder = CreateOrder(
            lockedCustomer.Id,
            now.UtcDateTime.AddDays(-2),
            OrderStatus.Cancelled,
            50m);
        var paidPayment = CreatePayment(activeOrder, PaymentStatus.Paid, now.UtcDateTime.AddDays(-1));
        var refundedPayment = CreatePayment(
            lockedOrder,
            PaymentStatus.Refunded,
            now.UtcDateTime.AddDays(-2));
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(),
            activeOrder.Id,
            activeCustomer.Id,
            "Sản phẩm bị lỗi.",
            now.UtcDateTime.AddHours(-2));

        context.AddRange(
            activeCustomer,
            lockedCustomer,
            staff,
            activeOrder,
            lockedOrder,
            paidPayment,
            refundedPayment,
            returnRequest);
        context.UserRoles.AddRange(
            UserRole.Create(activeCustomer.Id, customerRole),
            UserRole.Create(lockedCustomer.Id, customerRole),
            UserRole.Create(staff.Id, staffRole));
        context.PaymentStatusHistories.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = refundedPayment.Id,
            Payment = refundedPayment,
            FromStatus = PaymentStatus.Paid,
            ToStatus = PaymentStatus.Refunded,
            Source = PaymentStatusChangeSource.ManualRefund,
            Reference = "customer-refund",
            OccurredAt = now.UtcDateTime.AddDays(-1),
            CreatedAt = now.UtcDateTime.AddDays(-1)
        });
        await context.SaveChangesAsync();

        var service = TestServiceFactory.CreateCustomerManagementService(
            context,
            new FixedTimeProvider(now));
        var page = await service.GetCustomersAsync(new CustomerQueryParams
        {
            SortBy = "spent",
            SortOrder = "desc",
            Page = 1,
            PageSize = 10
        });
        var active = Assert.Single(
            page.Items,
            item => item.CustomerId == activeCustomer.Id);
        var locked = Assert.Single(
            page.Items,
            item => item.CustomerId == lockedCustomer.Id);
        var detail = await service.GetCustomerDetailAsync(activeCustomer.Id);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(100m, active.TotalSpent);
        Assert.Equal(1, active.OrderCount);
        Assert.Equal("Active", active.AccountStatus);
        Assert.Equal(0m, locked.TotalSpent);
        Assert.Equal("Locked", locked.AccountStatus);
        Assert.Equal(1, detail.TotalOrderCount);
        Assert.Equal(1, detail.CompletedOrderCount);
        Assert.Equal(1, detail.ReturnRequestCount);
        Assert.Equal(activeOrder.Id, detail.LastOrder?.OrderId);
        Assert.DoesNotContain(
            typeof(CustomerDetailResponse).GetProperties(),
            property => property.Name == nameof(User.PasswordHash));
    }

    [Fact]
    public async Task LockAndUnlockAsync_RevokeSessionsInvalidateTokensAndWriteAuditTrail()
    {
        var now = new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);
        await using var context = TestAppDbContext.Create();
        var customerRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Customer);
        var customer = CreateUser("managed_customer", now.UtcDateTime.AddDays(-1));
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            customer.Id,
            Guid.NewGuid(),
            new string('A', 64),
            now.UtcDateTime.AddHours(-1),
            now.UtcDateTime.AddDays(7));
        context.Add(customer);
        context.UserRoles.Add(UserRole.Create(customer.Id, customerRole));
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var audit = new RecordingAuditWriter();
        var service = TestServiceFactory.CreateCustomerManagementService(
            context,
            new FixedTimeProvider(now),
            audit);
        var actorId = Guid.NewGuid();

        var locked = await service.LockAsync(actorId, customer.Id);
        var repeatedLock = await service.LockAsync(actorId, customer.Id);
        var unlocked = await service.UnlockAsync(actorId, customer.Id);

        Assert.True(locked.Changed);
        Assert.Equal("Locked", locked.AccountStatus);
        Assert.Equal(DateTime.MaxValue, locked.LockedUntil);
        Assert.False(repeatedLock.Changed);
        Assert.True(unlocked.Changed);
        Assert.Equal("Active", unlocked.AccountStatus);
        Assert.Null(unlocked.LockedUntil);
        Assert.Equal(2, customer.TokenVersion);
        Assert.True(token.IsRevoked);
        Assert.Equal("Customer locked by administrator", token.RevocationReason);
        Assert.Equal(["customer.lock", "customer.unlock"], audit.Actions);
    }

    private static User CreateUser(string userName, DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            FullName = userName,
            CreatedAt = createdAt
        };

    private static Order CreateOrder(
        Guid userId,
        DateTime orderDate,
        OrderStatus status,
        decimal totalAmount)
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
        order.SetPricing(totalAmount, discount: 0, shipping: 0, tax: 0);

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

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<string> Actions { get; } = [];

        public void Write(
            string action,
            string entityType,
            string? entityId,
            Guid? actorUserId = null,
            IReadOnlyDictionary<string, object?>? metadata = null)
            => Actions.Add(action);
    }
}
