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
                PaymentStatus.Pending => next is PaymentStatus.RequiresAction
                    or PaymentStatus.Processing
                    or PaymentStatus.Paid
                    or PaymentStatus.Failed
                    or PaymentStatus.Cancelled,
                PaymentStatus.RequiresAction => next is PaymentStatus.Pending
                    or PaymentStatus.Processing
                    or PaymentStatus.Paid
                    or PaymentStatus.Failed
                    or PaymentStatus.Cancelled,
                PaymentStatus.Processing => next is PaymentStatus.Pending
                    or PaymentStatus.RequiresAction
                    or PaymentStatus.Paid
                    or PaymentStatus.Failed
                    or PaymentStatus.Cancelled,
                PaymentStatus.Paid => next is PaymentStatus.PartiallyRefunded
                    or PaymentStatus.Refunded,
                PaymentStatus.PartiallyRefunded => next == PaymentStatus.Refunded,
                PaymentStatus.Failed
                    or PaymentStatus.Cancelled
                    or PaymentStatus.Refunded => false,
                _ => false
            };
        }
    }
}
