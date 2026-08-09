using ECommerceBackend.Domain.Common;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Domain.Entities
{
    public class OrderDetail
    {
        internal OrderDetail()
        {
        }

        public Guid Id { get; internal set; }
        public Guid OrderId { get; internal set; }
        public Guid ProductId { get; internal set; }
        public string ProductNameSnapshot { get; internal set; } = string.Empty;
        public int Quantity { get; internal set; }
        public decimal UnitPrice { get; internal set; }

        // Navigation
        public Order? Order { get; set; }
        public Product? Product { get; set; }

        public static OrderDetail Create(
            Guid id,
            Guid orderId,
            Guid productId,
            string productNameSnapshot,
            int quantity,
            decimal unitPrice)
        {
            if (id == Guid.Empty || orderId == Guid.Empty
                || productId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "order_detail_identity_invalid",
                    "Thông tin định danh của dòng sản phẩm trong đơn hàng không hợp lệ.");
            }

            if (string.IsNullOrWhiteSpace(productNameSnapshot)
                || productNameSnapshot.Trim().Length > 200
                || quantity <= 0)
            {
                throw new DomainRuleViolationException(
                    "order_detail_snapshot_invalid",
                    "Thông tin sản phẩm hoặc số lượng trong đơn hàng không hợp lệ.");
            }

            OrderPricingPolicy.EnsureMoneyValue(
                unitPrice,
                "order_detail_unit_price_invalid",
                "Đơn giá sản phẩm");
            if (unitPrice <= 0)
            {
                throw new DomainRuleViolationException(
                    "order_detail_unit_price_invalid",
                    "Đơn giá sản phẩm trong đơn hàng phải lớn hơn 0.");
            }

            return new OrderDetail
            {
                Id = id,
                OrderId = orderId,
                ProductId = productId,
                ProductNameSnapshot = productNameSnapshot.Trim(),
                Quantity = quantity,
                UnitPrice = unitPrice
            };
        }
    }
}
