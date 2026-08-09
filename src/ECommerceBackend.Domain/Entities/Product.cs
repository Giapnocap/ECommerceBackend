using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class Product
    {
        internal Product()
        {
        }

        public Guid Id { get; internal set; }
        public Guid CategoryId { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public decimal Price { get; internal set; }
        public int StockQuantity { get; internal set; }
        public string Description { get; internal set; } = string.Empty;
        public bool IsDeleted { get; internal set; }
        public DateTime CreatedAt { get; internal set; } = DateTime.UtcNow;
        public byte[] RowVersion { get; internal set; } = [];

        // Navigation
        public Category? Category { get; set; }
        public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
        public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
        public ICollection<OrderDetail> OrderDetails { get; set; } = new List<OrderDetail>();
        public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = new List<InventoryTransaction>();

        public static Product Create(
            Guid id,
            Guid categoryId,
            string name,
            decimal price,
            int stockQuantity,
            string description,
            DateTime createdAt)
        {
            if (id == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "product_identity_invalid",
                    "Mã sản phẩm không hợp lệ.");
            }

            var product = new Product
            {
                Id = id,
                CreatedAt = createdAt
            };
            product.UpdateDetails(
                categoryId,
                name,
                price,
                description);
            _ = product.AdjustStockTo(stockQuantity);
            return product;
        }

        public void UpdateDetails(
            Guid categoryId,
            string name,
            decimal price,
            string description)
        {
            var details = ValidateDetails(
                categoryId,
                name,
                price,
                description);

            CategoryId = categoryId;
            Name = details.Name;
            Price = price;
            Description = details.Description;
        }

        public InventoryMutation AdjustStockTo(int targetQuantity)
            => InventoryPolicy.AdjustTo(this, targetQuantity);

        public bool MarkDeleted()
        {
            if (IsDeleted)
                return false;

            IsDeleted = true;
            return true;
        }

        private static ProductDetails ValidateDetails(
            Guid categoryId,
            string name,
            decimal price,
            string description)
        {
            if (categoryId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "product_category_invalid",
                    "Danh mục của sản phẩm không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainRuleViolationException(
                    "product_name_invalid",
                    "Tên sản phẩm không được để trống.");
            }

            var normalizedName = name.Trim();
            if (normalizedName.Length > 200)
            {
                throw new DomainRuleViolationException(
                    "product_name_invalid",
                    "Tên sản phẩm không được vượt quá 200 ký tự.");
            }

            OrderPricingPolicy.EnsureMoneyValue(
                price,
                "product_price_invalid",
                "Giá sản phẩm");
            if (price <= 0)
            {
                throw new DomainRuleViolationException(
                    "product_price_invalid",
                    "Giá sản phẩm phải lớn hơn 0.");
            }

            var normalizedDescription = description?.Trim() ?? string.Empty;
            if (normalizedDescription.Length > 2000)
            {
                throw new DomainRuleViolationException(
                    "product_description_invalid",
                    "Mô tả sản phẩm không được vượt quá 2000 ký tự.");
            }

            return new ProductDetails(
                normalizedName,
                normalizedDescription);
        }

        private readonly record struct ProductDetails(
            string Name,
            string Description);
    }
}
