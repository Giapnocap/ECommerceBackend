using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class PaymentStatusHistory
    {
        public Guid Id { get; set; }
        public Guid PaymentId { get; set; }
        public Guid? ChangedByUserId { get; set; }
        public PaymentStatus? FromStatus { get; set; }
        public PaymentStatus ToStatus { get; set; }
        public PaymentStatusChangeSource Source { get; set; }
        public string? Reference { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Payment? Payment { get; set; }
    }
}
