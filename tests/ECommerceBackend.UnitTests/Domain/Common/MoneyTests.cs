using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void CurrencyMetadata_AppliesCurrencySpecificRounding()
    {
        Assert.Equal(1_235m, Money.Round(1_234.5m, "VND").Amount);
        Assert.Equal(12.35m, Money.Round(12.345m, "USD").Amount);
        Assert.Equal(12.35m, Money.Round(12.345m, "EUR").Amount);
    }

    [Fact]
    public void CurrencyMetadata_ConvertsMinorUnitsWithoutHardcodedScale()
    {
        Assert.Equal(
            125_000,
            CurrencyCatalog.Get("VND").ToMinorUnits(125_000m));
        Assert.Equal(
            1_234,
            CurrencyCatalog.Get("USD").ToMinorUnits(12.34m));
        Assert.Equal(
            12.34m,
            CurrencyCatalog.Get("EUR").FromMinorUnits(1_234));
    }

    [Fact]
    public void Money_RejectsUnsupportedCurrencyAndInvalidScale()
    {
        Assert.Throws<DomainRuleViolationException>(() =>
            new Money(10.5m, "VND"));
        Assert.Throws<DomainRuleViolationException>(() =>
            new Money(10m, "GBP"));
    }

    [Fact]
    public void Order_PreservesBaseAndDisplayCurrencySnapshots()
    {
        var orderedAt = DateTime.UtcNow;
        var order = Order.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORD-20260818-MONEY-SNAPSHOT",
            Guid.NewGuid().ToString("N"),
            new string('A', 64),
            null,
            null,
            ShippingMethod.Standard,
            "USD",
            orderedAt,
            "1 Money Street",
            null);
        var baseAmounts = OrderPricingPolicy.CalculateAmounts(
            260_000m,
            10_000m,
            30_000m,
            0m);
        var displayAmounts = OrderPricingPolicy.CalculateAmounts(
            10m,
            0.38m,
            1.15m,
            0m);

        order.SetPricingSnapshot(
            "VND",
            0.00003846m,
            orderedAt.AddMinutes(-1),
            baseAmounts,
            displayAmounts);

        Assert.Equal("VND", order.BaseCurrency);
        Assert.Equal(280_000m, order.BaseTotalAmount);
        Assert.Equal("USD", order.Currency);
        Assert.Equal(10.77m, order.TotalAmount);
        Assert.Equal(0.00003846m, order.ExchangeRate);
        Assert.Equal(orderedAt.AddMinutes(-1), order.ExchangeRateCapturedAt);
    }
}
