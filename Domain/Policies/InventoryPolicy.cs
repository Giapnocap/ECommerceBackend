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
                    $"Product '{product.Name}' is no longer available.");
            }

            if (product.StockQuantity < quantity)
            {
                throw new DomainRuleViolationException(
                    "inventory_insufficient",
                    $"Product '{product.Name}' has insufficient stock. Available: {product.StockQuantity}, requested: {quantity}.");
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
                    $"Inventory balance for '{product.Name}' exceeds the supported value.");
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
                    "Inventory balance cannot be negative.");
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
                    "Inventory quantity must be greater than zero.");
            }
        }
    }
}
