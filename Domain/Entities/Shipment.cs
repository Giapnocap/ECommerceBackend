using ECommerceBackend.Domain.Common;

namespace ECommerceBackend.Domain.Entities
{
    public sealed class Shipment
    {
        public Guid Id { get; private set; }
        public Guid OrderId { get; private set; }
        public string Carrier { get; private set; } = string.Empty;
        public string TrackingNumber { get; private set; } = string.Empty;
        public Guid? CreatedByUserId { get; private set; }
        public DateTime ShippedAt { get; private set; }
        public DateTime? DeliveredAt { get; private set; }
        public byte[] RowVersion { get; set; } = [];

        public Order? Order { get; set; }
        public User? CreatedByUser { get; set; }

        public static Shipment Create(
            Guid id,
            Guid orderId,
            string carrier,
            string trackingNumber,
            Guid createdByUserId,
            DateTime shippedAt)
        {
            if (id == Guid.Empty || orderId == Guid.Empty
                || createdByUserId == Guid.Empty)
            {
                throw new DomainRuleViolationException(
                    "shipment_identity_invalid",
                    "Thông tin định danh của vận đơn không hợp lệ.");
            }

            return new Shipment
            {
                Id = id,
                OrderId = orderId,
                Carrier = NormalizeRequired(
                    carrier,
                    100,
                    "shipment_carrier_invalid",
                    "Đơn vị vận chuyển"),
                TrackingNumber = NormalizeRequired(
                    trackingNumber,
                    100,
                    "shipment_tracking_number_invalid",
                    "Mã vận đơn"),
                CreatedByUserId = createdByUserId,
                ShippedAt = shippedAt
            };
        }

        public void MarkDelivered(DateTime deliveredAt)
        {
            if (DeliveredAt.HasValue)
                return;

            if (deliveredAt < ShippedAt)
            {
                throw new DomainRuleViolationException(
                    "shipment_delivery_time_invalid",
                    "Thời điểm giao hàng không được trước thời điểm xuất hàng.");
            }

            DeliveredAt = deliveredAt;
        }

        public bool Matches(string carrier, string trackingNumber)
            => string.Equals(
                    Carrier,
                    carrier.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    TrackingNumber,
                    trackingNumber.Trim(),
                    StringComparison.OrdinalIgnoreCase);

        private static string NormalizeRequired(
            string value,
            int maximumLength,
            string code,
            string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new DomainRuleViolationException(
                    code,
                    $"{fieldName} không được để trống.");
            }

            var normalized = value.Trim();
            if (normalized.Length > maximumLength)
            {
                throw new DomainRuleViolationException(
                    code,
                    $"{fieldName} không được vượt quá {maximumLength} ký tự.");
            }

            return normalized;
        }
    }
}
