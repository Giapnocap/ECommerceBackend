namespace ECommerceBackend.Domain.Enums
{
    public static class OrderStatusTransitions
    {
        public const OrderStatus Initial = OrderStatus.Pending;

        public static bool CanTransitionTo(this OrderStatus current, OrderStatus next)
        {
            if (current == next)
                return true;

            return current switch
            {
                OrderStatus.Pending => next is OrderStatus.Confirmed or OrderStatus.Cancelled,
                OrderStatus.Confirmed => next is OrderStatus.Shipping or OrderStatus.Cancelled,
                OrderStatus.Shipping => next == OrderStatus.Delivered,
                OrderStatus.Delivered or OrderStatus.Cancelled => false,
                _ => false
            };
        }
    }
}
