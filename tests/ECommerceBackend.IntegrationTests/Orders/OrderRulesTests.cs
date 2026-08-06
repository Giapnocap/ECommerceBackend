using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Application.Validation;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public class OrderRulesTests
{
    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Confirmed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipping, false)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Shipping, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered, false)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.Shipping, OrderStatus.DeliveryFailed, true)]
    [InlineData(OrderStatus.DeliveryFailed, OrderStatus.Shipping, true)]
    [InlineData(OrderStatus.DeliveryFailed, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Delivered, OrderStatus.ReturnRequested, true)]
    [InlineData(OrderStatus.ReturnRequested, OrderStatus.ReturnApproved, true)]
    [InlineData(OrderStatus.ReturnRequested, OrderStatus.Delivered, true)]
    [InlineData(OrderStatus.ReturnApproved, OrderStatus.Returned, true)]
    [InlineData(OrderStatus.Returned, OrderStatus.Refunded, true)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Returned, false)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Returned, OrderStatus.Shipping, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed, false)]
    [InlineData(OrderStatus.Shipping, OrderStatus.Cancelled, false)]
    public void OrderStatusTransition_FollowsBusinessStateMachine(
        OrderStatus current,
        OrderStatus next,
        bool expected)
    {
        Assert.Equal(expected, current.CanTransitionTo(next));
    }

    [Fact]
    public void OrderLifecycle_InitialStatus_IsPending()
    {
        Assert.Equal(OrderStatus.Pending, OrderStatusTransitions.Initial);
        Assert.Equal(OrderStatus.Pending, new Order().Status);
    }

    [Fact]
    public void Return_RequiresCollectedPayment()
    {
        var order = new Order();
        order.ChangeStatus(OrderStatus.Confirmed, PaymentStatus.Pending);
        order.ChangeStatus(OrderStatus.Shipping, PaymentStatus.Pending);
        order.ChangeStatus(OrderStatus.Delivered, PaymentStatus.Pending);

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.ChangeStatus(
                OrderStatus.ReturnRequested,
                PaymentStatus.Pending));

        Assert.Equal("order_return_requires_paid_payment", exception.Code);
        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.DeliveryFailed)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.ReturnRequested)]
    [InlineData(OrderStatus.ReturnApproved)]
    [InlineData(OrderStatus.Refunded)]
    public void OrderStatusTransition_SameStatus_IsIdempotent(OrderStatus status)
    {
        Assert.True(status.CanTransitionTo(status));
    }

    [Fact]
    public void PlaceOrderValidator_RequiresShippingAddress()
    {
        var validator = new PlaceOrderRequestValidator();

        var result = validator.Validate(new PlaceOrderRequest
        {
            ShippingAddress = "",
            Note = "Please deliver during office hours."
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(PlaceOrderRequest.ShippingAddress));
    }

    [Fact]
    public void PlaceOrderValidator_RejectsInvalidRecipientContact()
    {
        var validator = new PlaceOrderRequestValidator();

        var result = validator.Validate(new PlaceOrderRequest
        {
            ShippingAddress = "Valid address",
            RecipientName = new string('a', 101),
            RecipientPhone = "123"
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(PlaceOrderRequest.RecipientName));
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(PlaceOrderRequest.RecipientPhone));
    }

    [Fact]
    public void OrderRecipientSnapshot_NormalizesAndProtectsContact()
    {
        var order = new Order();

        order.SetRecipient("  Nguyễn Văn A  ", " 0901234567 ");

        Assert.Equal("Nguyễn Văn A", order.RecipientName);
        Assert.Equal("0901234567", order.RecipientPhone);
        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            order.SetRecipient("Người nhận khác", "0907654321"));
        Assert.Equal("order_recipient_snapshot_immutable", exception.Code);
        Assert.Equal("Nguyễn Văn A", order.RecipientName);
        Assert.Equal("0901234567", order.RecipientPhone);

        var invalidOrder = new Order();
        var invalidPhone = Assert.Throws<DomainRuleViolationException>(() =>
            invalidOrder.SetRecipient("Người nhận", "invalid"));
        Assert.Equal("order_recipient_phone_invalid", invalidPhone.Code);
    }

    [Fact]
    public void PlaceOrderValidator_RejectsUndefinedPaymentMethod()
    {
        var validator = new PlaceOrderRequestValidator();

        var result = validator.Validate(new PlaceOrderRequest
        {
            ShippingAddress = "Valid address",
            PaymentMethod = (PaymentMethod)99
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(PlaceOrderRequest.PaymentMethod));
    }

    [Fact]
    public void UpdateOrderStatusValidator_RejectsInvalidEnumValue()
    {
        var validator = new UpdateOrderStatusRequestValidator();

        var result = validator.Validate(new UpdateOrderStatusRequest
        {
            Status = (OrderStatus)99
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOrderStatusRequest.Status));
    }

    [Fact]
    public void OrderMapping_UsesHistoricalProductSnapshotAndPaymentState()
    {
        var product = new Product { Id = Guid.NewGuid(), Name = "Current product name" };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            OrderNumber = "ORD-TEST-001",
            Payment = new Payment
            {
                Id = Guid.NewGuid(),
                Method = PaymentMethod.CashOnDelivery,
                Amount = 100
            },
            OrderDetails =
            [
                new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Product = product,
                    ProductNameSnapshot = "Purchased product name",
                    Quantity = 1,
                    UnitPrice = 100
                }
            ]
        };
        order.SetRecipient("Snapshot recipient", "0901234567");

        var response = order.ToResponse();

        Assert.Equal("Purchased product name", Assert.Single(response.OrderDetails).ProductName);
        Assert.Equal(nameof(PaymentStatus.Pending), response.Payment?.Status);
        Assert.Equal("Snapshot recipient", response.RecipientName);
        Assert.Equal("0901234567", response.RecipientPhone);
    }

    [Theory]
    [InlineData(
        PaymentStatus.Paid,
        "order_paid_cancellation_forbidden")]
    [InlineData(
        PaymentStatus.Refunded,
        "order_refunded_cancellation_forbidden")]
    public async Task UpdateStatusAsync_RejectsCancellationForSettledPayment(
        PaymentStatus paymentStatus,
        string expectedCode)
    {
        await using var context = TestAppDbContext.Create();
        var occurredAt = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "paid_customer",
            NormalizedUserName = "PAID_CUSTOMER",
            Email = "paid_customer@example.com",
            NormalizedEmail = "PAID_CUSTOMER@EXAMPLE.COM",
            FullName = "Paid Customer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = occurredAt
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IdempotencyRequestHash = new string('A', 64),
            OrderDate = occurredAt,
            ShippingAddress = "Address"
        };
        order.SetPricing(100m, discount: 0, shipping: 0, tax: 0);
        order.ChangeStatus(OrderStatus.Confirmed, null);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = PaymentMethod.CashOnDelivery,
            Amount = order.TotalAmount,
            Provider = "test",
            ProviderTransactionId = "paid-txn",
            CreatedAt = occurredAt
        };
        payment.ChangeStatus(PaymentStatus.Paid, occurredAt);
        if (paymentStatus == PaymentStatus.Refunded)
        {
            payment.ChangeStatus(
                PaymentStatus.Refunded,
                occurredAt.AddSeconds(1));
        }

        context.AddRange(user, order, payment);
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateOrderService(context);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateStatusAsync(
                order.Id,
                user.Id,
                new UpdateOrderStatusRequest { Status = OrderStatus.Cancelled }));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(paymentStatus, order.Payment?.Status);
    }

    [Fact]
    public void UpdateOrderStatusValidator_RequiresReasonForDeliveryFailure()
    {
        var validator = new UpdateOrderStatusRequestValidator();

        var result = validator.Validate(new UpdateOrderStatusRequest
        {
            Status = OrderStatus.DeliveryFailed
        });

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(UpdateOrderStatusRequest.Note));
    }

    [Theory]
    [InlineData(OrderStatus.Shipping)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.ReturnRequested)]
    [InlineData(OrderStatus.ReturnApproved)]
    [InlineData(OrderStatus.Returned)]
    [InlineData(OrderStatus.Refunded)]
    public async Task UpdateStatusAsync_RejectsManagedWorkflowTransitions(
        OrderStatus status)
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreateOrderService(context);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateStatusAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new UpdateOrderStatusRequest
                {
                    Status = status
                }));

        Assert.Equal("order_managed_transition_required", exception.Code);
    }

    [Fact]
    public async Task ReturnAndRefundAsync_RestoresStockAndRecordsManualRefundExactlyOnce()
    {
        await using var context = TestAppDbContext.Create();
        var occurredAt = DateTime.UtcNow.AddMinutes(-10);
        var actorUserId = Guid.NewGuid();
        var customer = new User
        {
            Id = Guid.NewGuid(),
            UserName = "return_customer",
            NormalizedUserName = "RETURN_CUSTOMER",
            Email = "return_customer@example.com",
            NormalizedEmail = "RETURN_CUSTOMER@EXAMPLE.COM",
            FullName = "Return Customer",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = occurredAt
        };
        var actor = new User
        {
            Id = actorUserId,
            UserName = "return_staff",
            NormalizedUserName = "RETURN_STAFF",
            Email = "return_staff@example.com",
            NormalizedEmail = "RETURN_STAFF@EXAMPLE.COM",
            FullName = "Return Staff",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Staff@123456"),
            CreatedAt = occurredAt
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Returned product",
            Description = "Returned product",
            Price = 100m,
            StockQuantity = 0,
            CreatedAt = occurredAt
        };
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            IdempotencyRequestHash = new string('R', 64),
            OrderDate = occurredAt,
            ShippingAddress = "Return address",
            OrderDetails =
            [
                new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    ProductNameSnapshot = product.Name,
                    Quantity = 1,
                    UnitPrice = product.Price
                }
            ]
        };
        order.SetPricing(100m, discount: 0, shipping: 0, tax: 0);
        order.ChangeStatus(OrderStatus.Confirmed, null);
        order.ChangeStatus(OrderStatus.Shipping, null);
        order.ChangeStatus(OrderStatus.Delivered, null);
        var shipment = Shipment.Create(
            Guid.NewGuid(),
            order.Id,
            "Giao Hàng Nhanh",
            "GHN-RETURN-001",
            actorUserId,
            occurredAt);
        shipment.MarkDelivered(occurredAt.AddMinutes(1));
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Method = PaymentMethod.CashOnDelivery,
            Amount = order.TotalAmount,
            Provider = "cod",
            ProviderTransactionId = order.OrderNumber,
            CreatedAt = occurredAt
        };
        payment.ChangeStatus(PaymentStatus.Paid, occurredAt.AddMinutes(1));

        context.AddRange(
            customer,
            actor,
            product,
            order,
            payment,
            shipment);
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateOrderService(context);

        var requestedOrder = await service.RequestReturnAsync(
            order.Id,
            customer.Id,
            new CreateReturnRequest
            {
                Reason = "Sản phẩm không còn phù hợp nhu cầu"
            });
        var approvedOrder = await service.ReviewReturnAsync(
            order.Id,
            actorUserId,
            new ReviewReturnRequest
            {
                Decision = ReturnReviewDecision.Approve,
                Note = "Đủ điều kiện trả hàng"
            });
        var returnedOrder = await service.ReceiveReturnAsync(
            order.Id,
            actorUserId,
            new ReceiveReturnRequest
            {
                InspectionNote = "Hàng nguyên vẹn, đủ phụ kiện"
            });
        var replayedReceive = await service.ReceiveReturnAsync(
            order.Id,
            actorUserId,
            new ReceiveReturnRequest
            {
                InspectionNote = "Hàng nguyên vẹn, đủ phụ kiện"
            });
        var firstRefund = await service.RecordRefundAsync(
            order.Id,
            actorUserId,
            new RecordOrderRefundRequest
            {
                Reference = "BANK-REFUND-001",
                Note = "Đã chuyển khoản"
            });
        var replayedRefund = await service.RecordRefundAsync(
            order.Id,
            actorUserId,
            new RecordOrderRefundRequest
            {
                Reference = "BANK-REFUND-001"
            });
        var mismatchedReplay = await Assert.ThrowsAsync<ConflictException>(() =>
            service.RecordRefundAsync(
                order.Id,
                actorUserId,
                new RecordOrderRefundRequest
                {
                    Reference = "BANK-REFUND-002"
                }));

        Assert.Equal(nameof(OrderStatus.ReturnRequested), requestedOrder.Status);
        Assert.Equal(nameof(OrderStatus.ReturnApproved), approvedOrder.Status);
        Assert.Equal(nameof(OrderStatus.Returned), returnedOrder.Status);
        Assert.Equal(nameof(OrderStatus.Returned), replayedReceive.Status);
        Assert.Equal(nameof(OrderStatus.Refunded), firstRefund.Status);
        Assert.Equal(nameof(PaymentStatus.Refunded), firstRefund.Payment?.Status);
        Assert.Equal(nameof(PaymentStatus.Refunded), replayedRefund.Payment?.Status);
        Assert.Equal(1, product.StockQuantity);
        var returnLedger = Assert.Single(context.InventoryTransactions.Where(
            transaction => transaction.Type == InventoryTransactionType.OrderReturned));
        Assert.Equal(1, returnLedger.QuantityChange);
        var refundHistory = Assert.Single(context.PaymentStatusHistories.Where(
            history => history.ToStatus == PaymentStatus.Refunded));
        Assert.Equal(PaymentStatusChangeSource.ManualRefund, refundHistory.Source);
        Assert.Equal("BANK-REFUND-001", refundHistory.Reference);
        Assert.Equal(
            ReturnRequestStatus.Refunded,
            Assert.Single(context.ReturnRequests).Status);
        Assert.Equal("refund_reference_mismatch", mismatchedReplay.Code);
    }
}
