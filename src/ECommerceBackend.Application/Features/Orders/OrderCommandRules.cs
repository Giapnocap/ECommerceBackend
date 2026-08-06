using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    internal static class OrderCommandRules
    {
        public static void EnsureGenericTransitionIsAllowed(
            OrderStatus requestedStatus)
        {
            if (requestedStatus is OrderStatus.Shipping
                or OrderStatus.Delivered
                or OrderStatus.ReturnRequested
                or OrderStatus.ReturnApproved
                or OrderStatus.Returned
                or OrderStatus.Refunded)
            {
                throw new ConflictException(
                    "order_managed_transition_required",
                    "Trạng thái giao hàng, trả hàng và hoàn tiền phải được cập nhật qua API nghiệp vụ tương ứng.");
            }
        }

        public static string GetStatusLabel(OrderStatus status)
            => status switch
            {
                OrderStatus.Pending => "Chờ xác nhận",
                OrderStatus.Confirmed => "Đã xác nhận",
                OrderStatus.Shipping => "Đang giao",
                OrderStatus.Delivered => "Đã giao",
                OrderStatus.Cancelled => "Đã hủy",
                OrderStatus.DeliveryFailed => "Giao thất bại",
                OrderStatus.Returned => "Đã nhận hàng hoàn",
                OrderStatus.ReturnRequested => "Đã yêu cầu trả hàng",
                OrderStatus.ReturnApproved => "Đã duyệt trả hàng",
                OrderStatus.Refunded => "Đã hoàn tiền",
                _ => status.ToString()
            };

        public static string? NormalizeOptional(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
