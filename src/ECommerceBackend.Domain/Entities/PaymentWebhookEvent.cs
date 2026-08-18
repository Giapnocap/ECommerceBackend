using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Domain.Entities
{
    public class PaymentWebhookEvent
    {
        public Guid Id { get; set; }
        public Guid PaymentId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string ProviderEventId { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string PayloadHash { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public PaymentStatus ResultingStatus { get; set; }
        public bool StatusChanged { get; set; }
        public DateTime OccurredAt { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
        public DateTime ProcessedAt { get; set; }

        public Payment? Payment { get; set; }
    }
}
