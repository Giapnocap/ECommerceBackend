using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public sealed class PromotionAnalyticsTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Analytics_UsesRedemptionTimeAndOrderPricingSnapshots()
    {
        await using var context = TestAppDbContext.Create();
        var user = CreateUser();
        var activePromotion = CreatePromotion(
            "ACTIVE10",
            Now.UtcDateTime.AddDays(-1),
            Now.UtcDateTime.AddDays(1),
            Now.UtcDateTime.AddDays(-2));
        var expiredPromotion = CreatePromotion(
            "EXPIRED5",
            Now.UtcDateTime.AddDays(-4),
            Now.UtcDateTime.AddDays(-1),
            Now.UtcDateTime.AddDays(-5));
        var firstOrder = CreateOrder(
            user.Id,
            activePromotion,
            Now.UtcDateTime.AddHours(-4),
            subtotal: 1_000m,
            discount: 100m);
        var secondOrder = CreateOrder(
            user.Id,
            activePromotion,
            Now.UtcDateTime.AddHours(-3),
            subtotal: 2_000m,
            discount: 200m);
        var historicalOrder = CreateOrder(
            user.Id,
            expiredPromotion,
            Now.UtcDateTime.AddDays(-2),
            subtotal: 500m,
            discount: 50m);
        activePromotion.Redeem(1_000m, Now.UtcDateTime.AddHours(-4), customerUsageCount: 0);
        activePromotion.Redeem(2_000m, Now.UtcDateTime.AddHours(-3), customerUsageCount: 1);
        expiredPromotion.Redeem(500m, Now.UtcDateTime.AddDays(-2), customerUsageCount: 0);

        context.AddRange(
            user,
            activePromotion,
            expiredPromotion,
            firstOrder,
            secondOrder,
            historicalOrder,
            new PromotionRedemption
            {
                Id = Guid.NewGuid(),
                PromotionId = activePromotion.Id,
                OrderId = firstOrder.Id,
                UserId = user.Id,
                DiscountAmount = 100m,
                CreatedAt = Now.UtcDateTime.AddHours(-4)
            },
            new PromotionRedemption
            {
                Id = Guid.NewGuid(),
                PromotionId = activePromotion.Id,
                OrderId = secondOrder.Id,
                UserId = user.Id,
                DiscountAmount = 200m,
                CreatedAt = Now.UtcDateTime.AddHours(-3)
            },
            new PromotionRedemption
            {
                Id = Guid.NewGuid(),
                PromotionId = expiredPromotion.Id,
                OrderId = historicalOrder.Id,
                UserId = user.Id,
                DiscountAmount = 50m,
                CreatedAt = Now.UtcDateTime.AddDays(-2)
            });
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreatePromotionService(
            context,
            new FixedTimeProvider(Now));
        var from = Now.UtcDateTime.AddDays(-1);
        var to = Now.UtcDateTime.AddDays(1);

        var detail = await service.GetAnalyticsAsync(
            activePromotion.Id,
            new PromotionAnalyticsRangeQuery { From = from, To = to });
        var page = await service.GetAnalyticsAsync(new PromotionAnalyticsQuery
        {
            From = from,
            To = to,
            SortBy = "discountAmount",
            Page = 1,
            PageSize = 10
        });

        Assert.Equal(activePromotion.Id, detail.PromotionId);
        Assert.Equal("ACTIVE10", detail.Code);
        Assert.Equal("Active", detail.Status);
        Assert.Equal(2, detail.UsageCount);
        Assert.Equal(2, detail.GeneratedOrderCount);
        Assert.Equal(3_000m, detail.GrossRevenue);
        Assert.Equal(300m, detail.DiscountAmount);
        Assert.Equal(2_700m, detail.NetRevenue);

        Assert.Equal(2, page.TotalCount);
        var ranked = page.Items.ToList();
        Assert.Equal(activePromotion.Id, ranked[0].PromotionId);
        Assert.Equal(expiredPromotion.Id, ranked[1].PromotionId);
        Assert.Equal("Expired", ranked[1].Status);
        Assert.Equal(0, ranked[1].UsageCount);
        Assert.Equal(0m, ranked[1].GrossRevenue);
    }

    [Fact]
    public async Task Analytics_RejectsUnsupportedRankingOutsideHttpValidation()
    {
        await using var context = TestAppDbContext.Create();
        var service = TestServiceFactory.CreatePromotionService(context);

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.GetAnalyticsAsync(new PromotionAnalyticsQuery { SortBy = "orders" }));

        Assert.Equal("promotion_analytics_sort_invalid", exception.Code);
    }

    private static User CreateUser()
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = "promotion_analytics_customer",
            NormalizedUserName = "PROMOTION_ANALYTICS_CUSTOMER",
            Email = "promotion.analytics.customer@example.com",
            NormalizedEmail = "PROMOTION.ANALYTICS.CUSTOMER@EXAMPLE.COM",
            FullName = "Promotion Analytics Customer",
            PasswordHash = "hash",
            CreatedAt = Now.UtcDateTime.AddDays(-10)
        };

    private static Promotion CreatePromotion(
        string code,
        DateTime startsAt,
        DateTime endsAt,
        DateTime createdAt)
        => Promotion.Create(
            Guid.NewGuid(),
            code,
            PromotionType.Percentage,
            value: 10m,
            minimumSubtotal: 0m,
            maximumDiscountAmount: null,
            startsAt,
            endsAt,
            usageLimit: 100,
            usageLimitPerCustomer: 10,
            occurredAt: createdAt);

    private static Order CreateOrder(
        Guid userId,
        Promotion promotion,
        DateTime orderDate,
        decimal subtotal,
        decimal discount)
    {
        var order = Order.Create(
            Guid.NewGuid(),
            userId,
            $"ORD-{Guid.NewGuid():N}"[..32],
            Guid.NewGuid().ToString("N"),
            new string('A', 64),
            promotion.Id,
            promotion.Code,
            ShippingMethod.Standard,
            "VND",
            orderDate,
            "Promotion analytics address",
            note: null);
        order.SetPricing(subtotal, discount, shipping: 0m, tax: 0m);
        return order;
    }
}
