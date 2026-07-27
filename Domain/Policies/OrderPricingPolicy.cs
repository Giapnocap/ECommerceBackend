using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Policies
{
    public readonly record struct OrderPricingLine(
        string ItemName,
        decimal UnitPrice,
        int Quantity);

    public readonly record struct OrderAmounts(
        decimal Subtotal,
        decimal Discount,
        decimal Shipping,
        decimal Tax,
        decimal Total);

    public readonly record struct OrderPricingRules(
        decimal StandardShippingFee,
        decimal ExpressShippingFee,
        decimal FreeStandardShippingMinimum,
        decimal TaxRatePercent);

    public static class OrderPricingPolicy
    {
        public const decimal MaxMoneyAmount = 9999999999999999.99m;

        public static decimal CalculateSubtotal(IEnumerable<OrderPricingLine> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);

            decimal subtotal = 0;
            var hasLines = false;
            foreach (var line in lines)
            {
                hasLines = true;
                EnsureMoney(line.UnitPrice, "order_unit_price_invalid", "Đơn giá");
                if (line.UnitPrice <= 0)
                {
                    throw new DomainRuleViolationException(
                        "order_unit_price_invalid",
                        $"Đơn giá của sản phẩm '{line.ItemName}' phải lớn hơn 0.");
                }

                if (line.Quantity <= 0)
                {
                    throw new DomainRuleViolationException(
                        "order_quantity_invalid",
                        $"Số lượng của sản phẩm '{line.ItemName}' phải lớn hơn 0.");
                }

                decimal lineTotal;
                try
                {
                    lineTotal = checked(line.UnitPrice * line.Quantity);
                }
                catch (OverflowException)
                {
                    throw new DomainRuleViolationException(
                        "order_line_total_exceeded",
                        $"Thành tiền của sản phẩm '{line.ItemName}' vượt quá giới hạn cho phép.");
                }

                if (lineTotal > MaxMoneyAmount
                    || MaxMoneyAmount - subtotal < lineTotal)
                {
                    throw new DomainRuleViolationException(
                        "order_total_exceeded",
                        "Tổng tiền đơn hàng vượt quá giới hạn cho phép.");
                }

                subtotal += lineTotal;
            }

            if (!hasLines)
            {
                throw new DomainRuleViolationException(
                    "order_empty",
                    "Đơn hàng phải có ít nhất một sản phẩm.");
            }

            return subtotal;
        }

        public static OrderAmounts CalculateAmounts(
            decimal subtotal,
            decimal discount,
            decimal shipping,
            decimal tax)
        {
            EnsureMoney(subtotal, "order_subtotal_invalid", "Tạm tính");
            EnsureMoney(discount, "order_discount_invalid", "Khoản giảm giá");
            EnsureMoney(shipping, "order_shipping_invalid", "Phí giao hàng");
            EnsureMoney(tax, "order_tax_invalid", "Thuế");

            if (subtotal <= 0)
            {
                throw new DomainRuleViolationException(
                    "order_subtotal_invalid",
                    "Tiền tạm tính của đơn hàng phải lớn hơn 0.");
            }

            if (discount < 0 || discount > subtotal)
            {
                throw new DomainRuleViolationException(
                    "order_discount_invalid",
                    "Khoản giảm giá phải nằm trong khoảng từ 0 đến tiền tạm tính.");
            }

            if (shipping < 0 || tax < 0)
            {
                throw new DomainRuleViolationException(
                    "order_charge_invalid",
                    "Phí giao hàng và thuế không được là số âm.");
            }

            decimal total;
            try
            {
                total = checked(subtotal - discount + shipping + tax);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "order_total_exceeded",
                    "Tổng tiền đơn hàng vượt quá giới hạn cho phép.");
            }

            if (total <= 0 || total > MaxMoneyAmount)
            {
                throw new DomainRuleViolationException(
                    "order_total_invalid",
                    "Tổng tiền đơn hàng phải lớn hơn 0 và không vượt quá giới hạn cho phép.");
            }

            return new OrderAmounts(subtotal, discount, shipping, tax, total);
        }

        public static OrderAmounts CalculateQuote(
            decimal subtotal,
            decimal discount,
            ShippingMethod shippingMethod,
            OrderPricingRules rules)
        {
            if (!Enum.IsDefined(shippingMethod))
            {
                throw new DomainRuleViolationException(
                    "shipping_method_invalid",
                    "Phương thức giao hàng không hợp lệ.");
            }

            EnsureMoney(
                rules.StandardShippingFee,
                "standard_shipping_fee_invalid",
                "Phí giao hàng tiêu chuẩn");
            EnsureMoney(
                rules.ExpressShippingFee,
                "express_shipping_fee_invalid",
                "Phí giao hàng nhanh");
            EnsureMoney(
                rules.FreeStandardShippingMinimum,
                "free_shipping_minimum_invalid",
                "Ngưỡng miễn phí giao hàng");
            EnsureMoney(
                rules.TaxRatePercent,
                "tax_rate_invalid",
                "Thuế suất");
            if (rules.TaxRatePercent > 100)
            {
                throw new DomainRuleViolationException(
                    "tax_rate_invalid",
                    "Thuế suất không được vượt quá 100 phần trăm.");
            }

            var taxableAmount = subtotal - discount;
            var shipping = shippingMethod switch
            {
                ShippingMethod.Standard
                    when taxableAmount >= rules.FreeStandardShippingMinimum
                    => 0,
                ShippingMethod.Standard => rules.StandardShippingFee,
                ShippingMethod.Express => rules.ExpressShippingFee,
                _ => throw new DomainRuleViolationException(
                    "shipping_method_invalid",
                    "Phương thức giao hàng không hợp lệ.")
            };
            decimal tax;
            try
            {
                tax = decimal.Round(
                    taxableAmount * rules.TaxRatePercent / 100m,
                    2,
                    MidpointRounding.AwayFromZero);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "order_tax_exceeded",
                    "Tiền thuế vượt quá giới hạn cho phép.");
            }

            return CalculateAmounts(
                subtotal,
                discount,
                shipping,
                tax);
        }

        public static void EnsureMoneyValue(
            decimal value,
            string code,
            string fieldName)
            => EnsureMoney(value, code, fieldName);

        private static void EnsureMoney(decimal value, string code, string fieldName)
        {
            if (value < 0
                || value > MaxMoneyAmount
                || decimal.Round(value, 2, MidpointRounding.ToEven) != value)
            {
                throw new DomainRuleViolationException(
                    code,
                    $"{fieldName} phải là số không âm và có tối đa 2 chữ số thập phân.");
            }
        }
    }
}
