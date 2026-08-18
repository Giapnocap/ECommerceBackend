using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class AdminDashboardService : IAdminDashboardService
    {
        private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
        private const int MaximumLowStockThreshold = 1_000_000;
        private const int MaximumTopProductLimit = 100;
        private const int MaximumRecentActivityLimit = 10;

        private readonly IAdminDashboardReadRepository _dashboardRepository;
        private readonly IReportReadRepository _reportRepository;
        private readonly IInventoryService _inventoryService;
        private readonly TimeProvider _timeProvider;

        public AdminDashboardService(
            IAdminDashboardReadRepository dashboardRepository,
            IReportReadRepository reportRepository,
            IInventoryService inventoryService,
            TimeProvider timeProvider)
        {
            _dashboardRepository = dashboardRepository;
            _reportRepository = reportRepository;
            _inventoryService = inventoryService;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<DashboardSummaryResponse> GetSummaryAsync(
            DashboardSummaryQuery query,
            CancellationToken cancellationToken = default)
        {
            ValidateLowStockThreshold(query.LowStockThreshold);

            var now = UtcNow;
            var todayStart = now.Date;
            var monthStart = new DateTime(
                now.Year,
                now.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);
            var metrics = await _dashboardRepository.GetSummaryMetricsAsync(
                todayStart,
                now,
                monthStart,
                now,
                query.LowStockThreshold,
                cancellationToken);

            return new DashboardSummaryResponse
            {
                GeneratedAt = now,
                RevenueToday = metrics.RevenueToday,
                RevenueThisMonth = metrics.RevenueThisMonth,
                OrdersToday = metrics.OrdersToday,
                TotalOrders = metrics.TotalOrders,
                PendingOrderCount = metrics.PendingOrderCount,
                CompletedOrderCount = metrics.CompletedOrderCount,
                CancelledOrderCount = metrics.CancelledOrderCount,
                OpenReturnRequestCount = metrics.OpenReturnRequestCount,
                TotalCustomerCount = metrics.TotalCustomerCount,
                NewCustomerCountThisMonth = metrics.NewCustomerCountThisMonth,
                LowStockThreshold = query.LowStockThreshold,
                LowStockProductCount = metrics.LowStockProductCount
            };
        }

        public async Task<DashboardRevenueTrendResponse> GetRevenueAsync(
            DashboardRevenueQuery query,
            CancellationToken cancellationToken = default)
        {
            var groupBy = NormalizeGroupBy(query.GroupBy);
            var (from, to) = ResolveRange(query.From, query.To);
            var dailyRows = await _reportRepository.GetRevenueDailyAggregatesAsync(
                from,
                to,
                cancellationToken);

            var items = dailyRows
                .GroupBy(row => GetPeriodStart(row.OccurredOn, groupBy))
                .OrderBy(group => group.Key)
                .Select(group => new DashboardRevenuePointResponse
                {
                    PeriodStart = group.Key,
                    PeriodEnd = GetPeriodEnd(group.Key, groupBy),
                    GrossRevenue = group.Sum(row => row.GrossRevenue),
                    RefundedAmount = group.Sum(row => row.RefundAmount),
                    NetRevenue = group.Sum(row => row.GrossRevenue - row.RefundAmount)
                })
                .ToList();

            return new DashboardRevenueTrendResponse
            {
                From = from,
                To = to,
                GroupBy = groupBy,
                Items = items
            };
        }

        public async Task<IReadOnlyList<StatusBreakdownResponse>> GetOrdersByStatusAsync(
            CancellationToken cancellationToken = default)
        {
            var rows = await _dashboardRepository.GetOrderStatusSummaryAsync(cancellationToken);
            return Enum.GetValues<OrderStatus>()
                .Select(status =>
                {
                    var row = rows.FirstOrDefault(item => item.Status == status);
                    return new StatusBreakdownResponse
                    {
                        Status = status.ToString(),
                        Count = row?.Count ?? 0,
                        Amount = row?.Amount ?? 0
                    };
                })
                .ToList();
        }

        public async Task<IReadOnlyList<TopSellingProductResponse>> GetTopProductsAsync(
            DashboardTopProductsQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.Limit is < 1 or > MaximumTopProductLimit)
            {
                throw new BusinessException(
                    "dashboard_top_product_limit_invalid",
                    "Số sản phẩm bán chạy phải từ 1 đến 100.");
            }

            var (from, to) = ResolveRange(query.From, query.To);
            return await _reportRepository.GetTopSellingProductsAsync(
                from,
                to,
                query.Limit,
                cancellationToken);
        }

        public Task<PagedResult<LowStockProductResponse>> GetLowStockAsync(
            LowStockQueryParams query,
            CancellationToken cancellationToken = default)
            => _inventoryService.GetLowStockAsync(query, cancellationToken);

        public async Task<IReadOnlyList<DashboardRecentActivityResponse>> GetRecentActivitiesAsync(
            DashboardRecentActivitiesQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.Limit is < 1 or > MaximumRecentActivityLimit)
            {
                throw new BusinessException(
                    "dashboard_recent_activity_limit_invalid",
                    "Số hoạt động gần đây phải từ 1 đến 10.");
            }

            var candidates = await _dashboardRepository.GetRecentActivitiesAsync(
                query.Limit,
                cancellationToken);

            return candidates
                .OrderByDescending(activity => activity.OccurredAt)
                .ThenByDescending(activity => activity.EntityId)
                .Take(query.Limit)
                .ToList();
        }

        private (DateTime From, DateTime To) ResolveRange(DateTime? requestedFrom, DateTime? requestedTo)
        {
            var to = NormalizeUtc(requestedTo ?? UtcNow);
            var from = NormalizeUtc(requestedFrom ?? to.AddDays(-30));
            if (from >= to)
            {
                throw new BusinessException(
                    "dashboard_range_invalid",
                    "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            }

            if (to - from > MaximumRange)
            {
                throw new BusinessException(
                    "dashboard_range_too_large",
                    "Khoảng thời gian dashboard không được vượt quá 366 ngày.");
            }

            return (from, to);
        }

        private static void ValidateLowStockThreshold(int threshold)
        {
            if (threshold is < 0 or > MaximumLowStockThreshold)
            {
                throw new BusinessException(
                    "dashboard_low_stock_threshold_invalid",
                    "Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
            }
        }

        private static string NormalizeGroupBy(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "day" or "week" or "month" => normalized,
                _ => throw new BusinessException(
                    "dashboard_group_by_invalid",
                    "Kiểu nhóm doanh thu phải là day, week hoặc month.")
            };
        }

        private static DateTime GetPeriodStart(DateTime occurredOn, string groupBy)
        {
            var day = NormalizeUtc(occurredOn).Date;
            return groupBy switch
            {
                "day" => day,
                "week" => day.AddDays(-((7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
                "month" => new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => throw new InvalidOperationException("Unsupported dashboard revenue grouping.")
            };
        }

        private static DateTime GetPeriodEnd(DateTime periodStart, string groupBy)
            => groupBy switch
            {
                "day" => periodStart.AddDays(1),
                "week" => periodStart.AddDays(7),
                "month" => periodStart.AddMonths(1),
                _ => throw new InvalidOperationException("Unsupported dashboard revenue grouping.")
            };

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
    }
}
