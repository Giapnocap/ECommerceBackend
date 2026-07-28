using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class OrderStatusHistory
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public OrderStatus? FromStatus { get; set; }
        public OrderStatus ToStatus { get; set; }
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Order? Order { get; set; }
        public User? ChangedByUser { get; set; }
    }
}
