using ECommerceBackend.Domain.Common;

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
                EnsureMoney(line.UnitPrice, "order_unit_price_invalid", "Unit price");
                if (line.UnitPrice <= 0)
                {
                    throw new DomainRuleViolationException(
                        "order_unit_price_invalid",
                        $"Unit price for '{line.ItemName}' must be greater than zero.");
                }

                if (line.Quantity <= 0)
                {
                    throw new DomainRuleViolationException(
                        "order_quantity_invalid",
                        $"Quantity for '{line.ItemName}' must be greater than zero.");
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
                        $"Line total for '{line.ItemName}' exceeds the supported amount.");
                }

                if (lineTotal > MaxMoneyAmount
                    || MaxMoneyAmount - subtotal < lineTotal)
                {
                    throw new DomainRuleViolationException(
                        "order_total_exceeded",
                        "Order total exceeds the supported amount.");
                }

                subtotal += lineTotal;
            }

            if (!hasLines)
            {
                throw new DomainRuleViolationException(
                    "order_empty",
                    "An order must contain at least one item.");
            }

            return subtotal;
        }

        public static OrderAmounts CalculateAmounts(
            decimal subtotal,
            decimal discount,
            decimal shipping,
            decimal tax)
        {
            EnsureMoney(subtotal, "order_subtotal_invalid", "Subtotal");
            EnsureMoney(discount, "order_discount_invalid", "Discount");
            EnsureMoney(shipping, "order_shipping_invalid", "Shipping fee");
            EnsureMoney(tax, "order_tax_invalid", "Tax");

            if (subtotal <= 0)
            {
                throw new DomainRuleViolationException(
                    "order_subtotal_invalid",
                    "Order subtotal must be greater than zero.");
            }

            if (discount < 0 || discount > subtotal)
            {
                throw new DomainRuleViolationException(
                    "order_discount_invalid",
                    "Order discount must be between zero and the subtotal.");
            }

            if (shipping < 0 || tax < 0)
            {
                throw new DomainRuleViolationException(
                    "order_charge_invalid",
                    "Shipping fee and tax cannot be negative.");
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
                    "Order total exceeds the supported amount.");
            }

            if (total <= 0 || total > MaxMoneyAmount)
            {
                throw new DomainRuleViolationException(
                    "order_total_invalid",
                    "Order total must be positive and within the supported amount.");
            }

            return new OrderAmounts(subtotal, discount, shipping, tax, total);
        }

        private static void EnsureMoney(decimal value, string code, string fieldName)
        {
            if (value < 0
                || value > MaxMoneyAmount
                || decimal.Round(value, 2, MidpointRounding.ToEven) != value)
            {
                throw new DomainRuleViolationException(
                    code,
                    $"{fieldName} must be a non-negative decimal with at most two fractional digits.");
            }
        }
    }
}