namespace ECommerceBackend.Domain.Enums
{
    public enum OrderStatus
    {
        Pending = 0,
        Confirmed = 1,
        Shipping = 2,
        Delivered = 3,
        Cancelled = 4,
        DeliveryFailed = 5,
        Returned = 6,
        ReturnRequested = 7,
        ReturnApproved = 8,
        Refunded = 9
    }
}
