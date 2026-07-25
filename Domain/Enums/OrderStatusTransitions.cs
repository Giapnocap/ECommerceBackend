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
                OrderStatus.Shipping => next is OrderStatus.Delivered or OrderStatus.DeliveryFailed,
                OrderStatus.DeliveryFailed => next is OrderStatus.Shipping or OrderStatus.Cancelled,
                OrderStatus.Delivered => next == OrderStatus.Returned,
                OrderStatus.Cancelled or OrderStatus.Returned => false,
                _ => false
            };
        }
    }
}
