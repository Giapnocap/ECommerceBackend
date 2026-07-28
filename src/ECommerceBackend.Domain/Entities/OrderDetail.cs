namespace ECommerceBackend.Domain.Entities
{
    public class OrderDetail
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid ProductId { get; set; }
        public string ProductNameSnapshot { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; } // Snapshot giá tại thời điểm đặt hàng

        // Navigation
        public Order? Order { get; set; }
        public Product? Product { get; set; }
    }
}
