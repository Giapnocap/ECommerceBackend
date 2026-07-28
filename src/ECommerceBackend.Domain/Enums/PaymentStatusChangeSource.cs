namespace ECommerceBackend.Domain.Enums
{
    public enum PaymentStatusChangeSource
    {
        Checkout = 0,
        OrderLifecycle = 1,
        Webhook = 2,
        LegacyBackfill = 3,
        ManualRefund = 4
    }
}
