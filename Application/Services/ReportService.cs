using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class ReportService : IReportService
    {
        private static readonly TimeSpan MaximumRange = TimeSpan.FromDays(366);
        private const int MaximumTopProductLimit = 100;
        private const int MaximumLowStockThreshold = 1_000_000;
        private readonly IAppDbContext _context;
        private readonly TimeProvider _timeProvider;

        public ReportService(IAppDbContext context)
            : this(context, TimeProvider.System)
        {
        }

        public ReportService(IAppDbContext context, TimeProvider timeProvider)
        {
            _context = context;
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

            var orderStatusRows = await _context.Orders
                .AsNoTracking()
                .Where(order => order.OrderDate >= from && order.OrderDate < to)
                .GroupBy(order => order.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.Count(),
                    Amount = group.Sum(order => order.TotalAmount)
                })
                .ToListAsync(cancellationToken);
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

            var deliveredOrders = await _context.OrderStatusHistories
                .AsNoTracking()
                .CountAsync(history => history.ToStatus == OrderStatus.Delivered
                    && history.CreatedAt >= from
                    && history.CreatedAt < to, cancellationToken);
            var cancelledOrders = await _context.OrderStatusHistories
                .AsNoTracking()
                .CountAsync(history => history.ToStatus == OrderStatus.Cancelled
                    && history.CreatedAt >= from
                    && history.CreatedAt < to, cancellationToken);

            var paymentStatusRows = await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.CreatedAt >= from && payment.CreatedAt < to)
                .GroupBy(payment => payment.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.Count(),
                    Amount = group.Sum(payment => payment.Amount)
                })
                .ToListAsync(cancellationToken);
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

            var grossPaidAmount = await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.PaidAt.HasValue
                    && payment.PaidAt.Value >= from
                    && payment.PaidAt.Value < to
                    && (payment.Status == PaymentStatus.Paid
                        || payment.Status == PaymentStatus.Refunded))
                .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;
            var refundedAmount = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && history.OccurredAt >= from
                    && history.OccurredAt < to)
                .SumAsync(history => (decimal?)history.Payment!.Amount, cancellationToken) ?? 0;
            var netRevenue = grossPaidAmount - refundedAmount;

            var lowStockCount = await _context.Products
                .AsNoTracking()
                .CountAsync(product => !product.IsDeleted
                    && product.StockQuantity <= query.LowStockThreshold,
                    cancellationToken);
            var eventFrom = from;
            var eventTo = to;
            var deliveredDetails =
                from detail in _context.OrderDetails.AsNoTracking()
                join deliveredHistory in _context.OrderStatusHistories.AsNoTracking()
                    on detail.OrderId equals deliveredHistory.OrderId
                where deliveredHistory.ToStatus == OrderStatus.Delivered
                    && deliveredHistory.CreatedAt >= eventFrom
                    && deliveredHistory.CreatedAt < eventTo
                select new
                {
                    detail.ProductId,
                    detail.ProductNameSnapshot,
                    detail.Quantity,
                    detail.UnitPrice,
                    DeliveredAt = deliveredHistory.CreatedAt,
                    detail.Id
                };
            var topProducts = await deliveredDetails
                .GroupBy(detail => detail.ProductId)
                .Select(group => new TopSellingProductResponse
                {
                    ProductId = group.Key,
                    ProductName = group
                        .OrderByDescending(detail => detail.DeliveredAt)
                        .ThenByDescending(detail => detail.Id)
                        .Select(detail => detail.ProductNameSnapshot)
                        .First(),
                    QuantitySold = group.Sum(detail => (long)detail.Quantity),
                    Revenue = group.Sum(detail => detail.UnitPrice * detail.Quantity)
                })
                .OrderByDescending(product => product.QuantitySold)
                .ThenByDescending(product => product.Revenue)
                .ThenBy(product => product.ProductId)
                .Take(query.TopProductLimit)
                .ToListAsync(cancellationToken);

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
