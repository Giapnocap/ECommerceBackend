using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Policies;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderPricingUseCase
    {
        private readonly ICartRepository _cartRepository;
        private readonly IPromotionRepository _promotionRepository;
        private readonly IExchangeRateProvider _exchangeRates;
        private readonly TimeProvider _timeProvider;
        private readonly PricingOptions _options;

        public OrderPricingUseCase(
            ICartRepository cartRepository,
            IPromotionRepository promotionRepository,
            IExchangeRateProvider exchangeRates,
            TimeProvider timeProvider,
            IOptions<PricingOptions> options)
        {
            _cartRepository = cartRepository;
            _promotionRepository = promotionRepository;
            _exchangeRates = exchangeRates;
            _timeProvider = timeProvider;
            _options = options.Value;
        }

        internal async Task<ExchangeRateQuote> GetExchangeRateAsync(
            string? requestedCurrency,
            CancellationToken cancellationToken = default)
        {
            var targetCurrency = string.IsNullOrWhiteSpace(
                requestedCurrency)
                    ? _options.Currency
                    : CurrencyCatalog.Normalize(requestedCurrency);
            if (!_options.SupportedCurrencies.Contains(
                    targetCurrency,
                    StringComparer.Ordinal))
            {
                throw new BusinessException(
                    "currency_not_enabled",
                    "Tiền tệ yêu cầu chưa được bật cho cửa hàng.");
            }

            var quote = await _exchangeRates.GetRateAsync(
                _options.Currency,
                targetCurrency,
                cancellationToken);
            if (!string.Equals(
                    quote.BaseCurrency,
                    _options.Currency,
                    StringComparison.Ordinal)
                || !string.Equals(
                    quote.QuoteCurrency,
                    targetCurrency,
                    StringComparison.Ordinal)
                || quote.Rate <= 0)
            {
                throw new ConflictException(
                    "exchange_rate_response_invalid",
                    "Dữ liệu tỷ giá không khớp yêu cầu báo giá.");
            }

            return quote;
        }

        public async Task<OrderQuoteResponse> GetQuoteAsync(
            Guid userId,
            OrderQuoteRequest request,
            CancellationToken cancellationToken = default)
        {
            var cart = await _cartRepository.GetByUserIdAsync(
                userId,
                cancellationToken)
                ?? throw new BusinessException("Không tìm thấy giỏ hàng.");
            if (cart.CartItems.Count == 0)
            {
                throw new BusinessException(
                    "Giỏ hàng trống. Vui lòng thêm sản phẩm trước khi tính giá.");
            }

            DomainRuleGuard.AsBusiness(() =>
                Cart.EnsureLineItemCountWithinLimit(
                    cart.CartItems.Count));

            foreach (var item in cart.CartItems)
            {
                if (item.Product == null)
                {
                    throw new BusinessException(
                        "Dữ liệu sản phẩm trong giỏ hàng không còn khả dụng.");
                }

                DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.EnsureCanReserve(
                        item.Product,
                        item.Quantity));
            }

            var occurredAt = _timeProvider.GetUtcNow().UtcDateTime;
            var exchangeRate = await GetExchangeRateAsync(
                request.Currency,
                cancellationToken);
            var calculation = await CalculateAsync(
                userId,
                cart.CartItems,
                request.PromotionCode,
                request.ShippingMethod,
                occurredAt,
                lockPromotion: false,
                exchangeRate,
                cancellationToken);
            var quoteExpiresAt = occurredAt.AddMinutes(
                _options.QuoteValidityMinutes);
            if (calculation.Promotion != null
                && calculation.Promotion.EndsAt < quoteExpiresAt)
            {
                quoteExpiresAt = calculation.Promotion.EndsAt;
            }

            return new OrderQuoteResponse
            {
                SubtotalAmount = calculation.Amounts.Subtotal,
                DiscountAmount = calculation.Amounts.Discount,
                ShippingFee = calculation.Amounts.Shipping,
                TaxAmount = calculation.Amounts.Tax,
                TotalAmount = calculation.Amounts.Total,
                Currency = calculation.Currency,
                BaseSubtotalAmount = calculation.BaseAmounts.Subtotal,
                BaseDiscountAmount = calculation.BaseAmounts.Discount,
                BaseShippingFee = calculation.BaseAmounts.Shipping,
                BaseTaxAmount = calculation.BaseAmounts.Tax,
                BaseTotalAmount = calculation.BaseAmounts.Total,
                BaseCurrency = calculation.BaseCurrency,
                ExchangeRate = calculation.ExchangeRate,
                ExchangeRateCapturedAt =
                    calculation.ExchangeRateCapturedAt,
                ShippingMethod = request.ShippingMethod.ToString(),
                PromotionCode = calculation.Promotion?.Code,
                CalculatedAt = occurredAt,
                ExpiresAt = quoteExpiresAt
            };
        }

        internal Task<OrderPricingCalculation> CalculateForCheckoutAsync(
            Guid userId,
            IEnumerable<CartItem> items,
            string? promotionCode,
            ShippingMethod shippingMethod,
            DateTime occurredAt,
            ExchangeRateQuote exchangeRate,
            CancellationToken cancellationToken = default)
            => CalculateAsync(
                userId,
                items,
                promotionCode,
                shippingMethod,
                occurredAt,
                lockPromotion: true,
                exchangeRate,
                cancellationToken);

        internal async Task RedeemAsync(
            OrderPricingCalculation calculation,
            Order order,
            Guid userId,
            DateTime occurredAt,
            CancellationToken cancellationToken = default)
        {
            if (calculation.Promotion == null)
                return;

            DomainRuleGuard.AsConflict(() =>
                calculation.Promotion.Redeem(
                    calculation.BaseAmounts.Subtotal,
                    occurredAt,
                    calculation.CustomerUsageCount));
            await _promotionRepository.AddRedemptionAsync(
                new PromotionRedemption
                {
                    Id = Guid.NewGuid(),
                    PromotionId = calculation.Promotion.Id,
                    OrderId = order.Id,
                    UserId = userId,
                    DiscountAmount = calculation.BaseAmounts.Discount,
                    CreatedAt = occurredAt
                },
                cancellationToken);
        }

        private async Task<OrderPricingCalculation> CalculateAsync(
            Guid userId,
            IEnumerable<CartItem> items,
            string? promotionCode,
            ShippingMethod shippingMethod,
            DateTime occurredAt,
            bool lockPromotion,
            ExchangeRateQuote exchangeRate,
            CancellationToken cancellationToken)
        {
            var itemList = items.ToArray();
            var subtotal = DomainRuleGuard.AsBusiness(() =>
                OrderPricingPolicy.CalculateSubtotal(
                    itemList.Select(item =>
                        new OrderPricingLine(
                            item.Product?.Name ?? string.Empty,
                            item.Product?.Price ?? 0,
                            item.Quantity))));

            Promotion? promotion = null;
            var customerUsageCount = 0;
            var discount = 0m;
            if (!string.IsNullOrWhiteSpace(promotionCode))
            {
                var normalizedCode = DomainRuleGuard.AsBusiness(() =>
                    Promotion.NormalizeCode(promotionCode));
                promotion = lockPromotion
                    ? await _promotionRepository.LockByNormalizedCodeAsync(
                        normalizedCode,
                        cancellationToken)
                    : await _promotionRepository.GetByNormalizedCodeAsync(
                        normalizedCode,
                        cancellationToken);
                if (promotion == null)
                {
                    throw new BusinessException(
                        "promotion_not_found",
                        "Mã khuyến mãi không tồn tại.");
                }

                customerUsageCount =
                    await _promotionRepository.CountCustomerRedemptionsAsync(
                        promotion.Id,
                        userId,
                        cancellationToken);
                discount = lockPromotion
                    ? DomainRuleGuard.AsConflict(() =>
                        promotion.CalculateDiscount(
                            subtotal,
                            occurredAt,
                            customerUsageCount))
                    : DomainRuleGuard.AsBusiness(() =>
                        promotion.CalculateDiscount(
                            subtotal,
                            occurredAt,
                            customerUsageCount));
            }

            var rules = new OrderPricingRules(
                _options.StandardShippingFee,
                _options.ExpressShippingFee,
                _options.FreeStandardShippingMinimum,
                _options.TaxRatePercent);
            var amounts = DomainRuleGuard.AsBusiness(() =>
                OrderPricingPolicy.CalculateQuote(
                    subtotal,
                    discount,
                    shippingMethod,
                    rules));
            var converted = ConvertAmounts(
                itemList,
                amounts,
                exchangeRate);
            return new OrderPricingCalculation(
                converted.Amounts,
                amounts,
                promotion,
                customerUsageCount,
                exchangeRate.BaseCurrency,
                exchangeRate.QuoteCurrency,
                exchangeRate.Rate,
                exchangeRate.CapturedAt,
                converted.UnitPrices);
        }

        private static ConvertedPricing ConvertAmounts(
            IReadOnlyCollection<CartItem> items,
            OrderAmounts baseAmounts,
            ExchangeRateQuote exchangeRate)
        {
            if (string.Equals(
                    exchangeRate.BaseCurrency,
                    exchangeRate.QuoteCurrency,
                    StringComparison.Ordinal))
            {
                return new ConvertedPricing(
                    baseAmounts,
                    items.ToDictionary(
                        item => item.ProductId,
                        item => item.Product!.Price));
            }

            var unitPrices = items.ToDictionary(
                item => item.ProductId,
                item => ConvertAmount(
                    item.Product!.Price,
                    exchangeRate));
            var subtotal = DomainRuleGuard.AsBusiness(() =>
                OrderPricingPolicy.CalculateSubtotal(
                    items.Select(item => new OrderPricingLine(
                        item.Product!.Name,
                        unitPrices[item.ProductId],
                        item.Quantity))));
            var discount = Math.Min(
                subtotal,
                ConvertAmount(baseAmounts.Discount, exchangeRate));
            var shipping = ConvertAmount(
                baseAmounts.Shipping,
                exchangeRate);
            var tax = ConvertAmount(baseAmounts.Tax, exchangeRate);
            var amounts = DomainRuleGuard.AsBusiness(() =>
                OrderPricingPolicy.CalculateAmounts(
                    subtotal,
                    discount,
                    shipping,
                    tax));
            return new ConvertedPricing(amounts, unitPrices);
        }

        private static decimal ConvertAmount(
            decimal amount,
            ExchangeRateQuote exchangeRate)
        {
            try
            {
                return Money.Round(
                    checked(amount * exchangeRate.Rate),
                    exchangeRate.QuoteCurrency).Amount;
            }
            catch (OverflowException)
            {
                throw new BusinessException(
                    "money_conversion_overflow",
                    "Số tiền sau quy đổi vượt quá giới hạn cho phép.");
            }
        }

        private sealed record ConvertedPricing(
            OrderAmounts Amounts,
            IReadOnlyDictionary<Guid, decimal> UnitPrices);
    }

    internal sealed record OrderPricingCalculation(
        OrderAmounts Amounts,
        OrderAmounts BaseAmounts,
        Promotion? Promotion,
        int CustomerUsageCount,
        string BaseCurrency,
        string Currency,
        decimal ExchangeRate,
        DateTime ExchangeRateCapturedAt,
        IReadOnlyDictionary<Guid, decimal> UnitPrices);
}
