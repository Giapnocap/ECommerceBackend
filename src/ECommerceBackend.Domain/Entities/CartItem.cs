using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class CartItem
    {
        public Guid Id { get; set; }
        public Guid CartId { get; internal set; }
        public Guid ProductId { get; internal set; }
        public int Quantity { get; internal set; }
        public decimal UnitPrice { get; internal set; }

        // Navigation
        public Cart? Cart { get; set; }
        public Product? Product { get; set; }

        public static CartItem Create(
            Guid id,
            Guid cartId,
            Product product,
            int quantity)
        {
            ArgumentNullException.ThrowIfNull(product);
            if (id == Guid.Empty || cartId == Guid.Empty
                || product.Id == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "cart_item_identity_invalid",
                    "Thông tin định danh của sản phẩm trong giỏ hàng không hợp lệ.");
            }

            var item = new CartItem
            {
                Id = id,
                CartId = cartId,
                ProductId = product.Id
            };
            item.SetQuantity(quantity, product);
            return item;
        }

        public void IncreaseQuantity(int quantity, Product product)
        {
            EnsurePositiveQuantity(quantity);

            var requestedQuantity = (long)Quantity + quantity;
            EnsureProductAvailable(product, requestedQuantity);
            Quantity = (int)requestedQuantity;
            UnitPrice = product.Price;
        }

        public void SetQuantity(int quantity, Product product)
        {
            EnsurePositiveQuantity(quantity);
            EnsureProductAvailable(product, quantity);
            Quantity = quantity;
            UnitPrice = product.Price;
        }

        public static void EnsurePositiveQuantity(int quantity)
        {
            if (quantity <= 0)
            {
                throw new DomainRuleViolationException(
                    "cart_quantity_invalid",
                    "Số lượng sản phẩm trong giỏ hàng phải lớn hơn 0.");
            }
        }

        public static void EnsureNonNegativeQuantity(int quantity)
        {
            if (quantity < 0)
            {
                throw new DomainRuleViolationException(
                    "cart_quantity_invalid",
                    "Số lượng sản phẩm trong giỏ hàng không được là số âm.");
            }
        }

        private void EnsureProductAvailable(
            Product product,
            long requestedQuantity)
        {
            ArgumentNullException.ThrowIfNull(product);
            if (product.Id != ProductId)
            {
                throw new DomainRuleViolationException(
                    "cart_product_mismatch",
                    "Sản phẩm không khớp với mặt hàng trong giỏ.");
            }

            if (product.IsDeleted)
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    "Sản phẩm đã ngừng bán. Vui lòng xóa sản phẩm khỏi giỏ hàng.");
            }

            if (requestedQuantity > product.StockQuantity)
            {
                throw new DomainRuleViolationException(
                    "business_error",
                    $"Sản phẩm '{product.Name}' chỉ còn {product.StockQuantity} trong kho.");
            }

            OrderPricingPolicy.EnsureMoneyValue(
                product.Price,
                "cart_unit_price_invalid",
                "Đơn giá trong giỏ hàng");
            if (product.Price <= 0)
            {
                throw new DomainRuleViolationException(
                    "cart_unit_price_invalid",
                    "Đơn giá trong giỏ hàng phải lớn hơn 0.");
            }
        }
    }
}
