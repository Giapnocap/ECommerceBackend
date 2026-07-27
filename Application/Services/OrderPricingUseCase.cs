using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderPricingUseCase
    {
        private readonly ICartRepository _cartRepository;
        private readonly IPromotionRepository _promotionRepository;
        private readonly TimeProvider _timeProvider;
        private readonly PricingOptions _options;

        public OrderPricingUseCase(
            ICartRepository cartRepository,
            IPromotionRepository promotionRepository,
            TimeProvider timeProvider,
            IOptions<PricingOptions> options)
        {
            _cartRepository = cartRepository;
            _promotionRepository = promotionRepository;
            _timeProvider = timeProvider;
            _options = options.Value;
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
            var calculation = await CalculateAsync(
                userId,
                cart.CartItems,
                request.PromotionCode,
                request.ShippingMethod,
                occurredAt,
                lockPromotion: false,
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
                Currency = _options.Currency,
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
            CancellationToken cancellationToken = default)
            => CalculateAsync(
                userId,
                items,
                promotionCode,
                shippingMethod,
                occurredAt,
                lockPromotion: true,
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
                    calculation.Amounts.Subtotal,
                    occurredAt,
                    calculation.CustomerUsageCount));
            await _promotionRepository.AddRedemptionAsync(
                new PromotionRedemption
                {
                    Id = Guid.NewGuid(),
                    PromotionId = calculation.Promotion.Id,
                    OrderId = order.Id,
                    UserId = userId,
                    DiscountAmount = calculation.Amounts.Discount,
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
            CancellationToken cancellationToken)
        {
            var subtotal = DomainRuleGuard.AsBusiness(() =>
                OrderPricingPolicy.CalculateSubtotal(
                    items.Select(item =>
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
            return new OrderPricingCalculation(
                amounts,
                promotion,
                customerUsageCount,
                _options.Currency);
        }
    }

    internal sealed record OrderPricingCalculation(
        OrderAmounts Amounts,
        Promotion? Promotion,
        int CustomerUsageCount,
        string Currency);
}
