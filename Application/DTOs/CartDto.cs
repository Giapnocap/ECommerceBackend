namespace ECommerceBackend.Application.DTOs
{
    public class CartItemResponse
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string? ProductImageUrl { get; set; }
        public decimal UnitPrice { get; set; }
        public int Quantity { get; set; }
        public bool IsAvailable { get; set; }
        public int AvailableStock { get; set; }
        public decimal SubTotal => UnitPrice * Quantity;
    }

    public class CartResponse
    {
        public Guid Id { get; set; }
        public IEnumerable<CartItemResponse> Items { get; set; } = Enumerable.Empty<CartItemResponse>();
        public decimal TotalAmount => Items.Where(i => i.IsAvailable).Sum(i => i.SubTotal);
        public int TotalItems => Items.Where(i => i.IsAvailable).Sum(i => i.Quantity);
    }

    public class AddToCartRequest
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class UpdateCartItemRequest
    {
        public int Quantity { get; set; }
    }
}
