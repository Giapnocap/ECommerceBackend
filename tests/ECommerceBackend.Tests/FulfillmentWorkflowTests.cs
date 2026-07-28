using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class FulfillmentWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DispatchAndDelivery_AreIdempotentAndCollectCodOnce()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedConfirmedOrderAsync(context);
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));
        var dispatchRequest = new DispatchShipmentRequest
        {
            Carrier = "Giao Hàng Nhanh",
            TrackingNumber = "GHN-001"
        };

        var dispatched = await service.DispatchShipmentAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            dispatchRequest);
        var replayedDispatch = await service.DispatchShipmentAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            dispatchRequest);
        var mismatch = await Assert.ThrowsAsync<ConflictException>(() =>
            service.DispatchShipmentAsync(
                fixture.Order.Id,
                fixture.Staff.Id,
                new DispatchShipmentRequest
                {
                    Carrier = "Giao Hàng Nhanh",
                    TrackingNumber = "GHN-OTHER"
                }));
        var delivered = await service.MarkShipmentDeliveredAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new MarkShipmentDeliveredRequest());
        var replayedDelivery = await service.MarkShipmentDeliveredAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new MarkShipmentDeliveredRequest());

        Assert.Equal(nameof(OrderStatus.Shipping), dispatched.Status);
        Assert.Equal(dispatched.Id, replayedDispatch.Id);
        Assert.Equal("shipment_identity_mismatch", mismatch.Code);
        Assert.Equal(nameof(OrderStatus.Delivered), delivered.Status);
        Assert.Equal(delivered.Id, replayedDelivery.Id);
        Assert.Equal(nameof(PaymentStatus.Paid), delivered.Payment?.Status);
        Assert.Single(await context.Shipments.ToListAsync());
        Assert.Equal(1, await context.OrderStatusHistories.CountAsync(
            history => history.OrderId == fixture.Order.Id
                && history.ToStatus == OrderStatus.Shipping));
        Assert.Equal(1, await context.OrderStatusHistories.CountAsync(
            history => history.OrderId == fixture.Order.Id
                && history.ToStatus == OrderStatus.Delivered));
        Assert.Equal(1, await context.PaymentStatusHistories.CountAsync(
            history => history.PaymentId == fixture.Payment.Id
                && history.ToStatus == PaymentStatus.Paid));
    }

    [Fact]
    public async Task RejectedReturn_RestoresDeliveredStateWithoutRestoringStock()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedDeliveredOrderAsync(context);
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));

        var requested = await service.RequestReturnAsync(
            fixture.Order.Id,
            fixture.Customer.Id,
            new CreateReturnRequest
            {
                Reason = "Không đúng nhu cầu sử dụng"
            });
        var rejected = await service.ReviewReturnAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new ReviewReturnRequest
            {
                Decision = ReturnReviewDecision.Reject,
                Note = "Sản phẩm đã có dấu hiệu sử dụng vượt chính sách"
            });
        var replay = await service.ReviewReturnAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new ReviewReturnRequest
            {
                Decision = ReturnReviewDecision.Reject,
                Note = "Sản phẩm đã có dấu hiệu sử dụng vượt chính sách"
            });

        Assert.Equal(nameof(OrderStatus.ReturnRequested), requested.Status);
        Assert.Equal(nameof(OrderStatus.Delivered), rejected.Status);
        Assert.Equal(rejected.Id, replay.Id);
        Assert.Equal(
            ReturnRequestStatus.Rejected,
            Assert.Single(await context.ReturnRequests.ToListAsync()).Status);
        Assert.Empty(await context.InventoryTransactions
            .Where(transaction =>
                transaction.Type == InventoryTransactionType.OrderReturned)
            .ToListAsync());
    }

    [Fact]
    public async Task ReturnRequest_AfterConfiguredWindow_IsRejected()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedDeliveredOrderAsync(
            context,
            Now.UtcDateTime.AddDays(-15));
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now),
            returnPolicyOptions: new ReturnPolicyOptions
            {
                ReturnWindowDays = 14
            });

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RequestReturnAsync(
                fixture.Order.Id,
                fixture.Customer.Id,
                new CreateReturnRequest
                {
                    Reason = "Yêu cầu quá hạn"
                }));

        Assert.Equal("return_window_expired", exception.Code);
        Assert.Equal(OrderStatus.Delivered, fixture.Order.Status);
        Assert.Empty(await context.ReturnRequests.ToListAsync());
    }

    [Fact]
    public async Task ReturnRequest_HidesAnotherCustomersOrder()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedDeliveredOrderAsync(context);
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.RequestReturnAsync(
                fixture.Order.Id,
                Guid.NewGuid(),
                new CreateReturnRequest
                {
                    Reason = "Không phải đơn của khách hiện tại"
                }));
    }

    private static async Task<FulfillmentFixture> SeedConfirmedOrderAsync(
        Infrastructure.Data.AppDbContext context)
    {
        var customer = CreateUser("fulfillment_customer");
        var staff = CreateUser("fulfillment_staff");
        var order = CreateOrder(customer.Id);
        order.ChangeStatus(OrderStatus.Confirmed, PaymentStatus.Pending);
        var payment = CreatePayment(order);

        context.AddRange(customer, staff, order, payment);
        await context.SaveChangesAsync();
        return new FulfillmentFixture(customer, staff, order, payment);
    }

    private static async Task<FulfillmentFixture> SeedDeliveredOrderAsync(
        Infrastructure.Data.AppDbContext context,
        DateTime? deliveredAt = null)
    {
        var fixture = await SeedConfirmedOrderAsync(context);
        var deliveryTime = deliveredAt ?? Now.UtcDateTime;
        var shipmentTime = deliveryTime.AddMinutes(-10);
        if (deliveryTime < fixture.Payment.CreatedAt)
        {
            fixture.Order.OrderDate = shipmentTime.AddDays(-1);
            fixture.Payment.CreatedAt = fixture.Order.OrderDate;
        }
        fixture.Order.ChangeStatus(OrderStatus.Shipping, PaymentStatus.Pending);
        fixture.Order.ChangeStatus(OrderStatus.Delivered, PaymentStatus.Pending);
        fixture.Payment.ChangeStatus(
            PaymentStatus.Paid,
            deliveryTime);
        var shipment = Shipment.Create(
            Guid.NewGuid(),
            fixture.Order.Id,
            "Viettel Post",
            $"VTP-{Guid.NewGuid():N}",
            fixture.Staff.Id,
            shipmentTime);
        shipment.MarkDelivered(deliveryTime);
        context.Shipments.Add(shipment);
        await context.SaveChangesAsync();
        return fixture;
    }

    private static User CreateUser(string userName)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FullName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
            CreatedAt = Now.UtcDateTime.AddDays(-30)
        };

    private static Order CreateOrder(Guid customerId)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = customerId,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IdempotencyRequestHash = new string('F', 64),
            OrderDate = Now.UtcDateTime.AddDays(-1),
            ShippingAddress = "1 Fulfillment Street"
        };
        order.SetPricing(100m, 0m, 0m, 0m);
        return order;
    }

    private static Payment CreatePayment(Order order)
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = PaymentMethod.CashOnDelivery,
            Amount = order.TotalAmount,
            Provider = "cod",
            ProviderTransactionId = order.OrderNumber,
            CreatedAt = order.OrderDate
        };

    private sealed record FulfillmentFixture(
        User Customer,
        User Staff,
        Order Order,
        Payment Payment);
}
