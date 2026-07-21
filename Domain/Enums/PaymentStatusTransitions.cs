namespace ECommerceBackend.Domain.Enums
{
    public static class PaymentStatusTransitions
    {
        public const PaymentStatus Initial = PaymentStatus.Pending;

        public static bool CanTransitionTo(this PaymentStatus current, PaymentStatus next)
        {
            if (current == next)
                return true;

            return current switch
            {
                PaymentStatus.Pending => next is PaymentStatus.Paid
                    or PaymentStatus.Failed
                    or PaymentStatus.Cancelled,
                PaymentStatus.Paid => next == PaymentStatus.Refunded,
                PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Refunded => false,
                _ => false
            };
        }
    }
}