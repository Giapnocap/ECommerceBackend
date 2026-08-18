using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;

namespace ECommerceBackend.Application.Services
{
    public sealed class ReportService : IReportService
    {
        private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
        private const int MaximumTopProductLimit = 100;
        private const int MaximumLowStockThreshold = 1_000_000;
        private readonly IReportReadRepository _reportRepository;
        private readonly TimeProvider _timeProvider;

        public ReportService(IReportReadRepository reportRepository)
            : this(reportRepository, TimeProvider.System)
        {
        }

        public ReportService(
            IReportReadRepository reportRepository,
            TimeProvider timeProvider)
        {
            _reportRepository = reportRepository;
            _timeProvider = timeProvider;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        public async Task<SalesSummaryResponse> GetSalesSummaryAsync(
            SalesSummaryQuery query,
            CancellationToken cancellationToken = default)
        {
            var to = NormalizeUtc(query.To ?? UtcNow);
            var from = NormalizeUtc(query.From ?? to.AddDays(-30));
            ValidateQuery(query, from, to);

            var orderStatusRows =
                await _reportRepository.GetOrderStatusSummaryAsync(
                    from,
                    to,
                    cancellationToken);
            var ordersByStatus = Enum.GetValues<OrderStatus>()
                .Select(status =>
                {
                    var row = orderStatusRows.FirstOrDefault(item => item.Status == status);
                    return new StatusBreakdownResponse
                    {
                        Status = status.ToString(),
                        Count = row?.Count ?? 0,
                        Amount = row?.Amount ?? 0
                    };
                })
                .ToList();

            var deliveredOrders =
                await _reportRepository.CountOrderTransitionsAsync(
                    OrderStatus.Delivered,
                    from,
                    to,
                    cancellationToken);
            var cancelledOrders =
                await _reportRepository.CountOrderTransitionsAsync(
                    OrderStatus.Cancelled,
                    from,
                    to,
                    cancellationToken);

            var paymentStatusRows =
                await _reportRepository.GetPaymentStatusSummaryAsync(
                    from,
                    to,
                    cancellationToken);
            var paymentsByStatus = Enum.GetValues<PaymentStatus>()
                .Select(status =>
                {
                    var row = paymentStatusRows.FirstOrDefault(item => item.Status == status);
                    return new StatusBreakdownResponse
                    {
                        Status = status.ToString(),
                        Count = row?.Count ?? 0,
                        Amount = row?.Amount ?? 0
                    };
                })
                .ToList();

            var grossPaidAmount =
                await _reportRepository.GetGrossPaidAmountAsync(
                    from,
                    to,
                    cancellationToken);
            var refundedAmount =
                await _reportRepository.GetRefundedAmountAsync(
                    from,
                    to,
                    cancellationToken);
            var netRevenue = grossPaidAmount - refundedAmount;

            var lowStockCount =
                await _reportRepository.CountLowStockProductsAsync(
                    query.LowStockThreshold,
                    cancellationToken);
            var topProducts =
                await _reportRepository.GetTopSellingProductsAsync(
                    from,
                    to,
                    query.TopProductLimit,
                    cancellationToken);

            var pendingPayment = paymentsByStatus.Single(item => item.Status == nameof(PaymentStatus.Pending));

            return new SalesSummaryResponse
            {
                From = from,
                To = to,
                TotalOrders = ordersByStatus.Sum(item => item.Count),
                DeliveredOrders = deliveredOrders,
                CancelledOrders = cancelledOrders,
                GrossPaidAmount = grossPaidAmount,
                RefundedAmount = refundedAmount,
                NetRevenue = netRevenue,
                PaidRevenue = netRevenue,
                PendingPaymentAmount = pendingPayment.Amount,
                LowStockThreshold = query.LowStockThreshold,
                LowStockProductCount = lowStockCount,
                OrdersByStatus = ordersByStatus,
                PaymentsByStatus = paymentsByStatus,
                TopSellingProducts = topProducts
            };
        }

        public async Task<RevenueReportResponse> GetRevenueReportAsync(
            RevenueReportQuery query,
            CancellationToken cancellationToken = default)
        {
            var groupBy = NormalizeRevenueGroupBy(query.GroupBy);
            var (from, to) = ResolveRange(query.From, query.To);
            var dailyRows = await _reportRepository.GetRevenueDailyAggregatesAsync(
                from,
                to,
                cancellationToken);
            var trend = dailyRows
                .GroupBy(row => GetPeriodStart(row.OccurredOn, groupBy))
                .OrderBy(group => group.Key)
                .Select(group => new RevenueReportPointResponse
                {
                    PeriodStart = group.Key,
                    PeriodEnd = GetPeriodEnd(group.Key, groupBy),
                    GrossRevenue = group.Sum(row => row.GrossRevenue),
                    RefundAmount = group.Sum(row => row.RefundAmount),
                    NetRevenue = group.Sum(row => row.GrossRevenue - row.RefundAmount),
                    OrderCount = group.Sum(row => row.OrderCount)
                })
                .ToList();
            var grossRevenue = trend.Sum(item => item.GrossRevenue);
            var refundAmount = trend.Sum(item => item.RefundAmount);
            var orderCount = trend.Sum(item => item.OrderCount);

            return new RevenueReportResponse
            {
                From = from,
                To = to,
                GroupBy = groupBy,
                GrossRevenue = grossRevenue,
                RefundAmount = refundAmount,
                NetRevenue = grossRevenue - refundAmount,
                OrderCount = orderCount,
                AverageOrderValue = DivideOrZero(grossRevenue, orderCount),
                Trend = trend
            };
        }

        public async Task<OrderReportResponse> GetOrderReportAsync(
            OrderReportQuery query,
            CancellationToken cancellationToken = default)
        {
            var (from, to) = ResolveRange(query.From, query.To);
            var rows = await _reportRepository.GetOrderStatusSummaryAsync(
                from,
                to,
                cancellationToken);
            var ordersByStatus = Enum.GetValues<OrderStatus>()
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
            var totalOrders = ordersByStatus.Sum(item => item.Count);
            var pendingOrders = CountOrders(ordersByStatus, OrderStatus.Pending);
            var deliveredOrders = CountOrders(ordersByStatus, OrderStatus.Delivered);
            var cancelledOrders = CountOrders(ordersByStatus, OrderStatus.Cancelled);
            var returnedOrders = CountOrders(ordersByStatus, OrderStatus.Returned)
                + CountOrders(ordersByStatus, OrderStatus.Refunded);

            return new OrderReportResponse
            {
                From = from,
                To = to,
                TotalOrders = totalOrders,
                PendingOrders = pendingOrders,
                DeliveredOrders = deliveredOrders,
                CancelledOrders = cancelledOrders,
                ReturnedOrders = returnedOrders,
                CompletionRatePercent = Percentage(deliveredOrders, totalOrders),
                CancellationRatePercent = Percentage(cancelledOrders, totalOrders),
                ReturnRatePercent = Percentage(returnedOrders, totalOrders),
                OrdersByStatus = ordersByStatus
            };
        }

        public async Task<ProductReportResponse> GetProductReportAsync(
            ProductReportQuery query,
            CancellationToken cancellationToken = default)
        {
            var (from, to) = ResolveRange(query.From, query.To);
            ValidateLowStockThreshold(query.LowStockThreshold);
            ValidateLimit(
                query.Limit,
                "report_product_limit_invalid",
                "Số sản phẩm phải từ 1 đến 100.");
            var lowStockProductCount = await _reportRepository.CountLowStockProductsAsync(
                query.LowStockThreshold,
                cancellationToken);
            var topProducts = await _reportRepository.GetTopSellingProductsAsync(
                from,
                to,
                query.Limit,
                cancellationToken);

            return new ProductReportResponse
            {
                From = from,
                To = to,
                LowStockThreshold = query.LowStockThreshold,
                LowStockProductCount = lowStockProductCount,
                TopSellingProducts = topProducts
            };
        }

        public async Task<CustomerReportResponse> GetCustomerReportAsync(
            CustomerReportQuery query,
            CancellationToken cancellationToken = default)
        {
            var (from, to) = ResolveRange(query.From, query.To);
            ValidateLimit(
                query.Limit,
                "report_customer_limit_invalid",
                "Số khách hàng phải từ 1 đến 100.");
            var newCustomerCount = await _reportRepository.CountNewCustomersAsync(
                from,
                to,
                cancellationToken);
            var customerOrderMetrics = await _reportRepository.GetCustomerOrderMetricsAsync(
                from,
                to,
                cancellationToken);
            var topCustomers = await _reportRepository.GetTopCustomersAsync(
                from,
                to,
                query.Limit,
                cancellationToken);

            return new CustomerReportResponse
            {
                From = from,
                To = to,
                NewCustomerCount = newCustomerCount,
                CustomersWithOrdersCount = customerOrderMetrics.CustomersWithOrdersCount,
                AverageOrdersPerCustomer = DivideOrZero(
                    customerOrderMetrics.OrderCount,
                    customerOrderMetrics.CustomersWithOrdersCount),
                TopCustomers = topCustomers
            };
        }

        public async Task<ReturnReportResponse> GetReturnReportAsync(
            ReturnReportQuery query,
            CancellationToken cancellationToken = default)
        {
            var (from, to) = ResolveRange(query.From, query.To);
            ValidateLimit(
                query.ReasonLimit,
                "report_return_reason_limit_invalid",
                "Số lý do trả hàng phải từ 1 đến 100.");
            var orderStatusRows = await _reportRepository.GetOrderStatusSummaryAsync(
                from,
                to,
                cancellationToken);
            var returnRequestCount = await _reportRepository.CountReturnRequestsAsync(
                from,
                to,
                cancellationToken);
            var refundAmount = await _reportRepository.GetRefundedAmountAsync(
                from,
                to,
                cancellationToken);
            var commonReasons = await _reportRepository.GetCommonReturnReasonsAsync(
                from,
                to,
                query.ReasonLimit,
                cancellationToken);
            var totalOrderCount = orderStatusRows.Sum(item => item.Count);

            return new ReturnReportResponse
            {
                From = from,
                To = to,
                TotalOrderCount = totalOrderCount,
                ReturnRequestCount = returnRequestCount,
                ReturnRatePercent = Percentage(returnRequestCount, totalOrderCount),
                RefundAmount = refundAmount,
                CommonReasons = commonReasons
            };
        }

        private static void ValidateQuery(SalesSummaryQuery query, DateTime from, DateTime to)
        {
            if (from >= to)
            {
                throw new BusinessException(
                    "report_range_invalid",
                    "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            }

            if (to - from > MaximumRange)
            {
                throw new BusinessException(
                    "report_range_too_large",
                    "Khoảng thời gian báo cáo không được vượt quá 366 ngày.");
            }

            if (query.LowStockThreshold is < 0 or > MaximumLowStockThreshold)
            {
                throw new BusinessException(
                    "report_low_stock_threshold_invalid",
                    "Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
            }

            if (query.TopProductLimit is < 1 or > MaximumTopProductLimit)
            {
                throw new BusinessException(
                    "report_top_product_limit_invalid",
                    "Số sản phẩm bán chạy phải từ 1 đến 100.");
            }
        }

        private (DateTime From, DateTime To) ResolveRange(DateTime? requestedFrom, DateTime? requestedTo)
        {
            var to = NormalizeUtc(requestedTo ?? UtcNow);
            var from = NormalizeUtc(requestedFrom ?? to.AddDays(-30));
            if (from >= to)
            {
                throw new BusinessException(
                    "report_range_invalid",
                    "Thời điểm bắt đầu phải nhỏ hơn thời điểm kết thúc.");
            }

            if (to - from > MaximumRange)
            {
                throw new BusinessException(
                    "report_range_too_large",
                    "Khoảng thời gian báo cáo không được vượt quá 366 ngày.");
            }

            return (from, to);
        }

        private static string NormalizeRevenueGroupBy(string? value)
        {
            var normalized = value?.Trim().ToLowerInvariant();
            return normalized switch
            {
                "day" or "week" or "month" => normalized,
                _ => throw new BusinessException(
                    "report_revenue_group_by_invalid",
                    "Kiểu nhóm doanh thu phải là day, week hoặc month.")
            };
        }

        private static void ValidateLowStockThreshold(int threshold)
        {
            if (threshold is < 0 or > MaximumLowStockThreshold)
            {
                throw new BusinessException(
                    "report_low_stock_threshold_invalid",
                    "Ngưỡng tồn kho phải từ 0 đến 1.000.000.");
            }
        }

        private static void ValidateLimit(int limit, string code, string message)
        {
            if (limit is < 1 or > MaximumTopProductLimit)
                throw new BusinessException(code, message);
        }

        private static int CountOrders(
            IEnumerable<StatusBreakdownResponse> rows,
            OrderStatus status)
            => rows.Single(item => item.Status == status.ToString()).Count;

        private static decimal DivideOrZero(decimal amount, int divisor)
            => divisor == 0
                ? 0
                : decimal.Round(amount / divisor, 2, MidpointRounding.AwayFromZero);

        private static decimal Percentage(int numerator, int denominator)
            => denominator == 0
                ? 0
                : decimal.Round(
                    (decimal)numerator / denominator * 100,
                    2,
                    MidpointRounding.AwayFromZero);

        private static DateTime GetPeriodStart(DateTime occurredOn, string groupBy)
        {
            var day = NormalizeUtc(occurredOn).Date;
            return groupBy switch
            {
                "day" => day,
                "week" => day.AddDays(-((7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
                "month" => new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => throw new InvalidOperationException("Unsupported revenue grouping.")
            };
        }

        private static DateTime GetPeriodEnd(DateTime periodStart, string groupBy)
            => groupBy switch
            {
                "day" => periodStart.AddDays(1),
                "week" => periodStart.AddDays(7),
                "month" => periodStart.AddMonths(1),
                _ => throw new InvalidOperationException("Unsupported revenue grouping.")
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
