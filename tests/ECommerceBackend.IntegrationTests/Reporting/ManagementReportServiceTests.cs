using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class ManagementReportServiceTests
{
    [Fact]
    public async Task ManagementReports_UseExplicitTimeAndStatusDefinitionsAcrossAnalytics()
    {
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(7);
        await using var context = TestAppDbContext.Create();
        var customerRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Customer);
        var staffRole = await context.Roles.SingleAsync(role => role.Name == RoleNames.Staff);
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Reporting category",
            NormalizedName = "REPORTING CATEGORY"
        };
        var bestSeller = CreateProduct(category.Id, "Best seller", stockQuantity: 12);
        var lowStock = CreateProduct(category.Id, "Low stock", stockQuantity: 3);
        var alice = CreateUser("report_alice", from.AddDays(-3));
        var bob = CreateUser("report_bob", from.AddDays(1));
        var newCustomer = CreateUser("report_new", from.AddDays(2));
        var staff = CreateUser("report_staff", from.AddDays(2));
        var deliveredOrder = CreateOrder(alice.Id, from.AddDays(1), OrderStatus.Delivered, 100m);
        var refundedOrder = CreateOrder(alice.Id, from.AddDays(2), OrderStatus.Refunded, 60m);
        var cancelledOrder = CreateOrder(bob.Id, from.AddDays(3), OrderStatus.Cancelled, 50m);
        var pendingOrder = CreateOrder(newCustomer.Id, from.AddDays(4), OrderStatus.Pending, 40m);
        var paidPayment = CreatePayment(deliveredOrder, PaymentStatus.Paid, from.AddDays(1).AddHours(1));
        var refundedPayment = CreatePayment(refundedOrder, PaymentStatus.Refunded, from.AddDays(2).AddHours(1));
        var returnRequestOne = ReturnRequest.Create(
            Guid.NewGuid(),
            deliveredOrder.Id,
            alice.Id,
            "Damaged item",
            from.AddDays(4));
        var returnRequestTwo = ReturnRequest.Create(
            Guid.NewGuid(),
            refundedOrder.Id,
            alice.Id,
            "Damaged item",
            from.AddDays(5));

        context.AddRange(
            category,
            bestSeller,
            lowStock,
            alice,
            bob,
            newCustomer,
            staff,
            deliveredOrder,
            refundedOrder,
            cancelledOrder,
            pendingOrder,
            paidPayment,
            refundedPayment,
            returnRequestOne,
            returnRequestTwo);
        context.UserRoles.AddRange(
            UserRole.Create(alice.Id, customerRole),
            UserRole.Create(bob.Id, customerRole),
            UserRole.Create(newCustomer.Id, customerRole),
            UserRole.Create(staff.Id, staffRole));
        context.OrderDetails.AddRange(
            CreateDetail(deliveredOrder.Id, bestSeller.Id, bestSeller.Name, quantity: 2, unitPrice: 50m),
            CreateDetail(refundedOrder.Id, lowStock.Id, lowStock.Name, quantity: 1, unitPrice: 60m));
        context.OrderStatusHistories.AddRange(
            CreateStatusHistory(deliveredOrder.Id, OrderStatus.Delivered, from.AddDays(1)),
            CreateStatusHistory(refundedOrder.Id, OrderStatus.Delivered, from.AddDays(2)));
        context.PaymentStatusHistories.Add(new PaymentStatusHistory
        {
            Id = Guid.NewGuid(),
            PaymentId = refundedPayment.Id,
            Payment = refundedPayment,
            FromStatus = PaymentStatus.Paid,
            ToStatus = PaymentStatus.Refunded,
            Source = PaymentStatusChangeSource.ManualRefund,
            Reference = "management-report-refund",
            OccurredAt = from.AddDays(2).AddHours(2),
            CreatedAt = from.AddDays(2).AddHours(2)
        });
        await context.SaveChangesAsync();

        var service = new ReportService(
            new ReportReadRepository(context),
            new FixedTimeProvider(new DateTimeOffset(to)));
        var revenue = await service.GetRevenueReportAsync(new RevenueReportQuery
        {
            From = from,
            To = to,
            GroupBy = "day"
        });
        var orders = await service.GetOrderReportAsync(new OrderReportQuery
        {
            From = from,
            To = to
        });
        var products = await service.GetProductReportAsync(new ProductReportQuery
        {
            From = from,
            To = to,
            Limit = 2,
            LowStockThreshold = 5
        });
        var customers = await service.GetCustomerReportAsync(new CustomerReportQuery
        {
            From = from,
            To = to,
            Limit = 2
        });
        var returns = await service.GetReturnReportAsync(new ReturnReportQuery
        {
            From = from,
            To = to,
            ReasonLimit = 2
        });

        Assert.Equal(160m, revenue.GrossRevenue);
        Assert.Equal(60m, revenue.RefundAmount);
        Assert.Equal(100m, revenue.NetRevenue);
        Assert.Equal(2, revenue.OrderCount);
        Assert.Equal(80m, revenue.AverageOrderValue);
        Assert.Equal(2, revenue.Trend.Count);

        Assert.Equal(4, orders.TotalOrders);
        Assert.Equal(1, orders.PendingOrders);
        Assert.Equal(1, orders.DeliveredOrders);
        Assert.Equal(1, orders.CancelledOrders);
        Assert.Equal(1, orders.ReturnedOrders);
        Assert.Equal(25m, orders.CompletionRatePercent);
        Assert.Equal(25m, orders.CancellationRatePercent);
        Assert.Equal(25m, orders.ReturnRatePercent);

        Assert.Equal(1, products.LowStockProductCount);
        Assert.Equal(2, products.TopSellingProducts.Count);
        var topProduct = products.TopSellingProducts[0];
        Assert.Equal(bestSeller.Id, topProduct.ProductId);
        Assert.Equal(2, topProduct.QuantitySold);
        Assert.Equal(100m, topProduct.Revenue);

        Assert.Equal(2, customers.NewCustomerCount);
        Assert.Equal(3, customers.CustomersWithOrdersCount);
        Assert.Equal(1.33m, customers.AverageOrdersPerCustomer);
        var topCustomer = Assert.Single(customers.TopCustomers);
        Assert.Equal(alice.Id, topCustomer.CustomerId);
        Assert.Equal(100m, topCustomer.TotalSpent);
        Assert.Equal(2, topCustomer.OrderCount);

        Assert.Equal(4, returns.TotalOrderCount);
        Assert.Equal(2, returns.ReturnRequestCount);
        Assert.Equal(50m, returns.ReturnRatePercent);
        Assert.Equal(60m, returns.RefundAmount);
        var commonReason = Assert.Single(returns.CommonReasons);
        Assert.Equal("Damaged item", commonReason.Reason);
        Assert.Equal(2, commonReason.Count);
    }

    [Fact]
    public async Task ManagementReports_RejectUnsafeInputOutsideHttpValidation()
    {
        var to = new DateTime(2026, 8, 18, 0, 0, 0, DateTimeKind.Utc);
        await using var context = TestAppDbContext.Create();
        var service = new ReportService(new ReportReadRepository(context));

        var invalidGrouping = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetRevenueReportAsync(new RevenueReportQuery
            {
                From = to.AddDays(-1),
                To = to,
                GroupBy = "year"
            }));
        var invalidProductLimit = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetProductReportAsync(new ProductReportQuery
            {
                From = to.AddDays(-1),
                To = to,
                Limit = 0
            }));
        var invalidCustomerLimit = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetCustomerReportAsync(new CustomerReportQuery
            {
                From = to.AddDays(-1),
                To = to,
                Limit = 101
            }));
        var invalidReturnRange = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetReturnReportAsync(new ReturnReportQuery
            {
                From = to,
                To = to
            }));

        Assert.Equal("report_revenue_group_by_invalid", invalidGrouping.Code);
        Assert.Equal("report_product_limit_invalid", invalidProductLimit.Code);
        Assert.Equal("report_customer_limit_invalid", invalidCustomerLimit.Code);
        Assert.Equal("report_range_invalid", invalidReturnRange.Code);
    }

    [Fact]
    public async Task ReportAggregations_NormalizeMixedCurrenciesToBaseCurrencySnapshots()
    {
        var from = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);
        await using var context = TestAppDbContext.Create();
        var customer = CreateUser("mixed_currency_customer", from.AddDays(-1));
        var vndOrder = CreateOrder(
            customer.Id,
            from.AddHours(1),
            OrderStatus.Pending,
            100m);
        var usdOrder = Order.Create(
            Guid.NewGuid(),
            customer.Id,
            $"ORD-{Guid.NewGuid():N}"[..32],
            Guid.NewGuid().ToString("N"),
            new string('A', 64),
            null,
            null,
            ShippingMethod.Standard,
            "USD",
            from.AddHours(2),
            "Address",
            null);
        usdOrder.SetPricingSnapshot(
            "VND",
            0.00004m,
            usdOrder.OrderDate,
            new OrderAmounts(2_500_000m, 0, 0, 0, 2_500_000m),
            new OrderAmounts(100m, 0, 0, 0, 100m));
        var vndPayment = CreatePayment(
            vndOrder,
            PaymentStatus.Paid,
            from.AddHours(3));
        var usdPayment = Payment.Create(
            Guid.NewGuid(),
            usdOrder.Id,
            PaymentMethod.Card,
            usdOrder.TotalAmount,
            "stripe",
            "pi_mixed_currency",
            usdOrder.OrderDate,
            usdOrder.Currency);
        usdPayment.ChangeStatus(PaymentStatus.Paid, from.AddHours(4));
        context.AddRange(
            customer,
            vndOrder,
            usdOrder,
            vndPayment,
            usdPayment);
        await context.SaveChangesAsync();
        var repository = new ReportReadRepository(context);

        var gross = await repository.GetGrossPaidAmountAsync(from, to);
        var orderStatuses = await repository.GetOrderStatusSummaryAsync(from, to);
        var paymentStatuses = await repository.GetPaymentStatusSummaryAsync(from, to);

        Assert.Equal(2_500_100m, gross);
        Assert.Equal(2_500_100m, Assert.Single(orderStatuses).Amount);
        Assert.Equal(2_500_100m, Assert.Single(paymentStatuses).Amount);
    }

    private static User CreateUser(string userName, DateTime createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FullName = userName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
            CreatedAt = createdAt
        };

    private static Product CreateProduct(Guid categoryId, string name, int stockQuantity)
        => new()
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Name = name,
            Description = name,
            Price = 100m,
            StockQuantity = stockQuantity,
            CreatedAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
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

        if (status is OrderStatus.Confirmed
            or OrderStatus.Shipping
            or OrderStatus.Delivered
            or OrderStatus.ReturnRequested
            or OrderStatus.ReturnApproved
            or OrderStatus.Returned
            or OrderStatus.Refunded)
        {
            order.ChangeStatus(OrderStatus.Confirmed, null);
        }

        if (status is OrderStatus.Shipping
            or OrderStatus.Delivered
            or OrderStatus.ReturnRequested
            or OrderStatus.ReturnApproved
            or OrderStatus.Returned
            or OrderStatus.Refunded)
        {
            order.ChangeStatus(OrderStatus.Shipping, null);
        }

        if (status is OrderStatus.Delivered
            or OrderStatus.ReturnRequested
            or OrderStatus.ReturnApproved
            or OrderStatus.Returned
            or OrderStatus.Refunded)
        {
            order.ChangeStatus(OrderStatus.Delivered, null);
        }

        if (status == OrderStatus.Cancelled)
            order.ChangeStatus(OrderStatus.Cancelled, null);
        if (status is OrderStatus.ReturnRequested
            or OrderStatus.ReturnApproved
            or OrderStatus.Returned
            or OrderStatus.Refunded)
        {
            order.ChangeStatus(OrderStatus.ReturnRequested, PaymentStatus.Paid);
        }

        if (status is OrderStatus.ReturnApproved
            or OrderStatus.Returned
            or OrderStatus.Refunded)
        {
            order.ChangeStatus(OrderStatus.ReturnApproved, PaymentStatus.Paid);
        }

        if (status is OrderStatus.Returned or OrderStatus.Refunded)
            order.ChangeStatus(OrderStatus.Returned, PaymentStatus.Paid);
        if (status == OrderStatus.Refunded)
            order.ChangeStatus(OrderStatus.Refunded, PaymentStatus.Refunded);

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
            Quantity = quantity,
            UnitPrice = unitPrice,
            BaseUnitPrice = unitPrice
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
            payment.ChangeStatus(PaymentStatus.Refunded, paidAt.AddHours(1));

        return payment;
    }
}
