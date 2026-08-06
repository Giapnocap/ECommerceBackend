using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IReportReadRepository
    {
        Task<IReadOnlyList<StatusSummary<OrderStatus>>> GetOrderStatusSummaryAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<int> CountOrderTransitionsAsync(
            OrderStatus status,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StatusSummary<PaymentStatus>>> GetPaymentStatusSummaryAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<decimal> GetGrossPaidAmountAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<decimal> GetRefundedAmountAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<int> CountLowStockProductsAsync(
            int threshold,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TopSellingProductResponse>> GetTopSellingProductsAsync(
            DateTime from,
            DateTime to,
            int limit,
            CancellationToken cancellationToken = default);
    }
}
