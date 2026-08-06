using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public class Cart
    {
        public const int MaximumLineItems = 50;

        public Guid Id { get; set; }
        public Guid UserId { get; internal set; }

        // Navigation
        public User? User { get; set; }
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();

        public static Cart Create(Guid id, Guid userId)
        {
            if (id == Guid.Empty || userId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "cart_identity_invalid",
                    "Thông tin định danh của giỏ hàng không hợp lệ.");
            }

            return new Cart
            {
                Id = id,
                UserId = userId
            };
        }

        public CartItem AddItem(
            Guid itemId,
            Product product,
            int quantity)
        {
            ArgumentNullException.ThrowIfNull(product);
            if (CartItems.Any(item => item.ProductId == product.Id))
            {
                throw new DomainRuleViolationException(
                    "cart_item_duplicate",
                    "Sản phẩm đã tồn tại trong giỏ hàng.");
            }

            EnsureLineItemCountWithinLimit(CartItems.Count + 1);

            var item = CartItem.Create(
                itemId,
                Id,
                product,
                quantity);
            CartItems.Add(item);
            return item;
        }

        public void RemoveItem(CartItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.CartId != Id || !CartItems.Remove(item))
            {
                throw new DomainRuleViolationException(
                    "cart_item_not_owned",
                    "Sản phẩm không thuộc giỏ hàng này.");
            }
        }

        public IReadOnlyList<CartItem> ClearItems()
        {
            var removedItems = CartItems.ToArray();
            CartItems.Clear();
            return removedItems;
        }

        public static void EnsureLineItemCountWithinLimit(int lineItemCount)
        {
            if (lineItemCount <= MaximumLineItems)
                return;

            throw new DomainRuleViolationException(
                "cart_line_item_limit_exceeded",
                $"Giỏ hàng chỉ được chứa tối đa {MaximumLineItems} sản phẩm khác nhau. "
                + "Vui lòng xóa bớt sản phẩm trước khi tiếp tục.");
        }
    }
}
