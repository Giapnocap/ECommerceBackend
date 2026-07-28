using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Domain.Policies
{
    public readonly record struct InventoryMutation(
        int QuantityChange,
        int BalanceAfter);

    public static class InventoryPolicy
    {
        public static InventoryMutation Reserve(Product product, int quantity)
        {
            EnsureCanReserve(product, quantity);

            product.StockQuantity -= quantity;
            return new InventoryMutation(-quantity, product.StockQuantity);
        }

        public static void EnsureCanReserve(Product product, int quantity)
        {
            ArgumentNullException.ThrowIfNull(product);
            EnsurePositiveQuantity(quantity);

            if (product.IsDeleted)
            {
                throw new DomainRuleViolationException(
                    "inventory_product_unavailable",
                    $"Sản phẩm '{product.Name}' không còn khả dụng.");
            }

            if (product.StockQuantity < quantity)
            {
                throw new DomainRuleViolationException(
                    "inventory_insufficient",
                    $"Sản phẩm '{product.Name}' không đủ tồn kho. Hiện có: {product.StockQuantity}, yêu cầu: {quantity}.");
            }
        }

        public static InventoryMutation Release(Product product, int quantity)
        {
            ArgumentNullException.ThrowIfNull(product);
            EnsurePositiveQuantity(quantity);

            try
            {
                product.StockQuantity = checked(product.StockQuantity + quantity);
            }
            catch (OverflowException)
            {
                throw new DomainRuleViolationException(
                    "inventory_balance_exceeded",
                    $"Tồn kho của sản phẩm '{product.Name}' vượt quá giới hạn cho phép.");
            }

            return new InventoryMutation(quantity, product.StockQuantity);
        }

        public static InventoryMutation AdjustTo(Product product, int targetBalance)
        {
            ArgumentNullException.ThrowIfNull(product);
            if (targetBalance < 0)
            {
                throw new DomainRuleViolationException(
                    "inventory_balance_invalid",
                    "Tồn kho không được là số âm.");
            }

            var quantityChange = targetBalance - product.StockQuantity;
            product.StockQuantity = targetBalance;
            return new InventoryMutation(quantityChange, targetBalance);
        }

        private static void EnsurePositiveQuantity(int quantity)
        {
            if (quantity <= 0)
            {
                throw new DomainRuleViolationException(
                    "inventory_quantity_invalid",
                    "Số lượng tồn kho phải lớn hơn 0.");
            }
        }
    }
}
