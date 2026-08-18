using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IAdminDashboardService
    {
        Task<DashboardSummaryResponse> GetSummaryAsync(
            DashboardSummaryQuery query,
            CancellationToken cancellationToken = default);

        Task<DashboardRevenueTrendResponse> GetRevenueAsync(
            DashboardRevenueQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StatusBreakdownResponse>> GetOrdersByStatusAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<TopSellingProductResponse>> GetTopProductsAsync(
            DashboardTopProductsQuery query,
            CancellationToken cancellationToken = default);

        Task<PagedResult<LowStockProductResponse>> GetLowStockAsync(
            LowStockQueryParams query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DashboardRecentActivityResponse>> GetRecentActivitiesAsync(
            DashboardRecentActivitiesQuery query,
            CancellationToken cancellationToken = default);
    }
}
