using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface IAdminDashboardReadRepository
    {
        Task<DashboardSummaryMetrics> GetSummaryMetricsAsync(
            DateTime todayStart,
            DateTime todayEnd,
            DateTime monthStart,
            DateTime now,
            int lowStockThreshold,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StatusSummary<OrderStatus>>> GetOrderStatusSummaryAsync(
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DashboardRecentActivityResponse>> GetRecentActivitiesAsync(
            int limitPerSource,
            CancellationToken cancellationToken = default);
    }
}
