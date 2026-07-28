namespace ECommerceBackend.Domain.Entities
{
    public sealed class PromotionRedemption
    {
        public Guid Id { get; set; }
        public Guid PromotionId { get; set; }
        public Guid OrderId { get; set; }
        public Guid UserId { get; set; }
        public decimal DiscountAmount { get; set; }
        public DateTime CreatedAt { get; set; }

        public Promotion? Promotion { get; set; }
        public Order? Order { get; set; }
        public User? User { get; set; }
    }
}
