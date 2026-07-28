using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class InventoryTransaction
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid? OrderId { get; set; }
        public Guid? CreatedByUserId { get; set; }
        public InventoryTransactionType Type { get; set; }
        public int QuantityChange { get; set; }
        public int BalanceAfter { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Product? Product { get; set; }
        public Order? Order { get; set; }
        public User? CreatedByUser { get; set; }
    }
}
