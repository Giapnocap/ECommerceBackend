using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Tests;

public sealed class PricingAndPromotionTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PricingPolicy_CalculatesConfiguredShippingAndTax()
    {
        var rules = new OrderPricingRules(
            StandardShippingFee: 30_000m,
            ExpressShippingFee: 60_000m,
            FreeStandardShippingMinimum: 1_000_000m,
            TaxRatePercent: 10m);

        var standard = OrderPricingPolicy.CalculateQuote(
            subtotal: 1_200_000m,
            discount: 100_000m,
            ShippingMethod.Standard,
            rules);
        var express = OrderPricingPolicy.CalculateQuote(
            subtotal: 1_200_000m,
            discount: 300_000m,
            ShippingMethod.Express,
            rules);

        Assert.Equal(0m, standard.Shipping);
        Assert.Equal(110_000m, standard.Tax);
        Assert.Equal(1_210_000m, standard.Total);
        Assert.Equal(60_000m, express.Shipping);
        Assert.Equal(90_000m, express.Tax);
        Assert.Equal(1_050_000m, express.Total);
    }

    [Fact]
    public void Promotion_ProtectsPeriodSubtotalAndUsageLimits()
    {
        var promotion = CreatePromotion(
            usageLimit: 1,
            usageLimitPerCustomer: 1);

        Assert.Throws<DomainRuleViolationException>(() =>
            promotion.CalculateDiscount(
                subtotal: 400_000m,
                Now.UtcDateTime,
                customerUsageCount: 0));

        var discount = promotion.CalculateDiscount(
            subtotal: 1_000_000m,
            Now.UtcDateTime,
            customerUsageCount: 0);
        promotion.Redeem(
            subtotal: 1_000_000m,
            Now.UtcDateTime,
            customerUsageCount: 0);

        Assert.Equal(80_000m, discount);
        Assert.Equal(1, promotion.UsedCount);
        var exhausted = Assert.Throws<DomainRuleViolationException>(() =>
            promotion.CalculateDiscount(
                subtotal: 1_000_000m,
                Now.UtcDateTime,
                customerUsageCount: 0));
        Assert.Equal("promotion_usage_limit_reached", exhausted.Code);
    }

    [Fact]
    public async Task QuoteAndCheckout_RecalculateAndPersistPricingSnapshotOnce()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context);
        var promotion = CreatePromotion(
            usageLimit: 10,
            usageLimitPerCustomer: 1);
        context.Promotions.Add(promotion);
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now),
            pricingOptions: PricingOptions());

        var quote = await service.GetQuoteAsync(
            fixture.User.Id,
            new OrderQuoteRequest
            {
                ShippingMethod = ShippingMethod.Standard,
                PromotionCode = " save10 "
            });

        Assert.Equal(1_000_000m, quote.SubtotalAmount);
        Assert.Equal(80_000m, quote.DiscountAmount);
        Assert.Equal(30_000m, quote.ShippingFee);
        Assert.Equal(92_000m, quote.TaxAmount);
        Assert.Equal(1_042_000m, quote.TotalAmount);
        Assert.Equal("SAVE10", quote.PromotionCode);
        Assert.Equal("VND", quote.Currency);
        Assert.Equal(Now.UtcDateTime.AddMinutes(5), quote.ExpiresAt);
        Assert.Equal(0, promotion.UsedCount);

        var request = new PlaceOrderRequest
        {
            ShippingAddress = "1 Pricing Street",
            PaymentMethod = PaymentMethod.CashOnDelivery,
            ShippingMethod = ShippingMethod.Standard,
            PromotionCode = "save10",
            ExpectedTotalAmount = quote.TotalAmount
        };
        var changedPrice = await Assert.ThrowsAsync<ConflictException>(() =>
            service.PlaceOrderAsync(
                fixture.User.Id,
                new PlaceOrderRequest
                {
                    ShippingAddress = request.ShippingAddress,
                    PaymentMethod = request.PaymentMethod,
                    ShippingMethod = request.ShippingMethod,
                    PromotionCode = request.PromotionCode,
                    ExpectedTotalAmount =
                        quote.TotalAmount - 1
                },
                "pricing-checkout"));
        Assert.Equal(
            "checkout_price_changed",
            changedPrice.Code);
        Assert.Empty(await context.Orders.ToListAsync());
        Assert.Single(await context.CartItems.ToListAsync());
        Assert.Equal(0, promotion.UsedCount);

        var placed = await service.PlaceOrderAsync(
            fixture.User.Id,
            request,
            "pricing-checkout");
        var replay = await service.PlaceOrderAsync(
            fixture.User.Id,
            request,
            "pricing-checkout");

        Assert.Equal(placed.Id, replay.Id);
        Assert.Equal(quote.SubtotalAmount, placed.SubtotalAmount);
        Assert.Equal(quote.DiscountAmount, placed.DiscountAmount);
        Assert.Equal(quote.ShippingFee, placed.ShippingFee);
        Assert.Equal(quote.TaxAmount, placed.TaxAmount);
        Assert.Equal(quote.TotalAmount, placed.TotalAmount);
        Assert.Equal("SAVE10", placed.PromotionCode);
        Assert.Equal(nameof(ShippingMethod.Standard), placed.ShippingMethod);
        Assert.Equal("VND", placed.Currency);
        Assert.Equal(1, promotion.UsedCount);
        Assert.Single(await context.PromotionRedemptions.ToListAsync());
        Assert.Empty(await context.CartItems.ToListAsync());
        var mismatchedReplay =
            await Assert.ThrowsAsync<ConflictException>(() =>
                service.PlaceOrderAsync(
                    fixture.User.Id,
                    new PlaceOrderRequest
                    {
                        ShippingAddress = request.ShippingAddress,
                        PaymentMethod = request.PaymentMethod,
                        ShippingMethod =
                            ShippingMethod.Express,
                        PromotionCode = request.PromotionCode
                    },
                    "pricing-checkout"));
        Assert.Equal("conflict", mismatchedReplay.Code);

        await AddCartItemAsync(
            context,
            fixture.Cart.Id,
            fixture.Product.Id);
        var customerLimit = await Assert.ThrowsAsync<ConflictException>(() =>
            service.PlaceOrderAsync(
                fixture.User.Id,
                request,
                "pricing-second-order"));
        Assert.Equal(
            "promotion_customer_limit_reached",
            customerLimit.Code);
        Assert.Single(await context.Orders.ToListAsync());
        Assert.Single(await context.CartItems.ToListAsync());
        Assert.Equal(1, promotion.UsedCount);
    }

    [Fact]
    public async Task QuoteAndCheckout_WithUsd_PersistImmutableBaseAndOrderCurrencySnapshots()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCheckoutAsync(context);
        var promotion = CreatePromotion(
            usageLimit: 10,
            usageLimitPerCustomer: 1);
        context.Promotions.Add(promotion);
        await context.SaveChangesAsync();
        var exchangeRates = new TestExchangeRateProvider(
            new FixedTimeProvider(Now),
            new Dictionary<(string Base, string Quote), decimal>
            {
                [("VND", "USD")] = 0.00004m
            });
        var options = PricingOptions();
        options.SupportedCurrencies = ["VND", "USD"];
        var service = TestServiceFactory.CreateOrderService(
            context,
            new FixedTimeProvider(Now),
            pricingOptions: options,
            exchangeRateProvider: exchangeRates);

        var quote = await service.GetQuoteAsync(
            fixture.User.Id,
            new OrderQuoteRequest
            {
                ShippingMethod = ShippingMethod.Standard,
                PromotionCode = "SAVE10",
                Currency = "usd"
            });

        Assert.Equal("VND", quote.BaseCurrency);
        Assert.Equal("USD", quote.Currency);
        Assert.Equal(0.00004m, quote.ExchangeRate);
        Assert.Equal(Now.UtcDateTime, quote.ExchangeRateCapturedAt);
        Assert.Equal(1_000_000m, quote.BaseSubtotalAmount);
        Assert.Equal(80_000m, quote.BaseDiscountAmount);
        Assert.Equal(30_000m, quote.BaseShippingFee);
        Assert.Equal(92_000m, quote.BaseTaxAmount);
        Assert.Equal(1_042_000m, quote.BaseTotalAmount);
        Assert.Equal(40m, quote.SubtotalAmount);
        Assert.Equal(3.20m, quote.DiscountAmount);
        Assert.Equal(1.20m, quote.ShippingFee);
        Assert.Equal(3.68m, quote.TaxAmount);
        Assert.Equal(41.68m, quote.TotalAmount);

        var placed = await service.PlaceOrderAsync(
            fixture.User.Id,
            new PlaceOrderRequest
            {
                ShippingAddress = "1 Multi Currency Street",
                PaymentMethod = PaymentMethod.CashOnDelivery,
                ShippingMethod = ShippingMethod.Standard,
                PromotionCode = "SAVE10",
                ExpectedTotalAmount = quote.TotalAmount,
                Currency = "USD"
            },
            "usd-checkout");

        context.ChangeTracker.Clear();
        var order = await context.Orders
            .Include(item => item.OrderDetails)
            .SingleAsync(item => item.Id == placed.Id);
        var detail = Assert.Single(order.OrderDetails);
        var payment = await context.Payments
            .SingleAsync(item => item.OrderId == order.Id);
        var redemption = await context.PromotionRedemptions
            .SingleAsync(item => item.OrderId == order.Id);

        Assert.Equal("VND", order.BaseCurrency);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(0.00004m, order.ExchangeRate);
        Assert.Equal(1_042_000m, order.BaseTotalAmount);
        Assert.Equal(41.68m, order.TotalAmount);
        Assert.Equal(500_000m, detail.BaseUnitPrice);
        Assert.Equal(20m, detail.UnitPrice);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal(41.68m, payment.Amount);
        Assert.Equal(80_000m, redemption.DiscountAmount);
    }

    [Fact]
    public async Task PromotionService_KeepsCodeImmutableAndRejectsLimitBelowUsage()
    {
        await using var context = TestAppDbContext.Create();
        var actor = new User
        {
            Id = Guid.NewGuid(),
            UserName = "promotion_admin",
            NormalizedUserName = "PROMOTION_ADMIN",
            Email = "promotion.admin@example.com",
            NormalizedEmail = "PROMOTION.ADMIN@EXAMPLE.COM",
            FullName = "Promotion Admin",
            PasswordHash = "hash",
            CreatedAt = Now.UtcDateTime
        };
        var promotion = CreatePromotion(
            usageLimit: 2,
            usageLimitPerCustomer: 2);
        promotion.Redeem(
            1_000_000m,
            Now.UtcDateTime,
            customerUsageCount: 0);
        context.AddRange(actor, promotion);
        await context.SaveChangesAsync();
        var service = TestServiceFactory.CreatePromotionService(
            context,
            new FixedTimeProvider(Now.AddMinutes(1)));

        var exception = await Assert.ThrowsAsync<BusinessException>(() =>
            service.UpdateAsync(
                promotion.Id,
                new UpdatePromotionRequest
                {
                    Type = PromotionType.Percentage,
                    Value = 15,
                    MinimumSubtotal = 500_000,
                    MaximumDiscountAmount = 100_000,
                    StartsAt = Now.UtcDateTime.AddDays(-1),
                    EndsAt = Now.UtcDateTime.AddDays(1),
                    UsageLimit = 0,
                    UsageLimitPerCustomer = 1,
                    IsActive = true
                },
                actor.Id));

        Assert.Equal(
            "promotion_usage_limit_below_used_count",
            exception.Code);
        Assert.Equal("SAVE10", promotion.Code);
        Assert.Equal(10m, promotion.Value);
    }

    private static PricingOptions PricingOptions()
        => new()
        {
            Currency = "VND",
            SupportedCurrencies = ["VND"],
            QuoteValidityMinutes = 5,
            StandardShippingFee = 30_000m,
            ExpressShippingFee = 60_000m,
            FreeStandardShippingMinimum = 1_000_000m,
            TaxRatePercent = 10m
        };

    private static Promotion CreatePromotion(
        int usageLimit,
        int usageLimitPerCustomer)
        => Promotion.Create(
            Guid.NewGuid(),
            "SAVE10",
            PromotionType.Percentage,
            value: 10m,
            minimumSubtotal: 500_000m,
            maximumDiscountAmount: 80_000m,
            startsAt: Now.UtcDateTime.AddDays(-1),
            endsAt: Now.UtcDateTime.AddDays(1),
            usageLimit,
            usageLimitPerCustomer,
            Now.UtcDateTime);

    private static async Task<(
        User User,
        Cart Cart,
        Product Product)> SeedCheckoutAsync(
            Infrastructure.Data.AppDbContext context)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "pricing_customer",
            NormalizedUserName = "PRICING_CUSTOMER",
            Email = "pricing.customer@example.com",
            NormalizedEmail = "PRICING.CUSTOMER@EXAMPLE.COM",
            FullName = "Pricing Customer",
            PasswordHash = "hash",
            CreatedAt = Now.UtcDateTime
        };
        var cart = new Cart
        {
            Id = Guid.NewGuid(),
            UserId = user.Id
        };
        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = "Pricing",
            NormalizedName = "PRICING"
        };
        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = category.Id,
            Name = "Pricing Product",
            Price = 500_000m,
            StockQuantity = 10,
            CreatedAt = Now.UtcDateTime
        };
        var item = new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cart.Id,
            ProductId = product.Id,
            Quantity = 2,
            UnitPrice = product.Price
        };
        context.AddRange(user, cart, category, product, item);
        await context.SaveChangesAsync();
        return (user, cart, product);
    }

    private static async Task AddCartItemAsync(
        Infrastructure.Data.AppDbContext context,
        Guid cartId,
        Guid productId)
    {
        context.CartItems.Add(new CartItem
        {
            Id = Guid.NewGuid(),
            CartId = cartId,
            ProductId = productId,
            Quantity = 1,
            UnitPrice = 500_000m
        });
        await context.SaveChangesAsync();
    }
}
