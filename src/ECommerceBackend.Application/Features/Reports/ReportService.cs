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

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
            };
    }
}
