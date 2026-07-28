namespace ECommerceBackend.Application.Common
{
    public static class OutboxMessageTypes
    {
        public const string NotificationRequested = "notification.requested.v1";
        public const string ProtectedNotificationRequested =
            "notification.protected-requested.v1";
    }

    public sealed record NotificationRequestedPayload(
        Guid UserId,
        string Subject,
        string Message,
        Guid? OrderId = null,
        Guid? PaymentId = null);
}
