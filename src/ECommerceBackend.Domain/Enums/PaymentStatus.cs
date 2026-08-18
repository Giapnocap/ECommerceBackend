namespace ECommerceBackend.Domain.Enums
{
    public enum PaymentStatus
    {
        Pending = 0,
        Paid = 1,
        Failed = 2,
        Cancelled = 3,
        Refunded = 4,
        RequiresAction = 5,
        Processing = 6,
        PartiallyRefunded = 7
    }
}
