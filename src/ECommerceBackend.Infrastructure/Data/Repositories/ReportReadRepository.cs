using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class ReportReadRepository : IReportReadRepository
    {
        private readonly AppDbContext _context;

        public ReportReadRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<StatusSummary<OrderStatus>>> GetOrderStatusSummaryAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            var rows = await _context.Orders
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
            return rows
                .Select(row => new StatusSummary<OrderStatus>(
                    row.Status,
                    row.Count,
                    row.Amount))
                .ToList();
        }

        public Task<int> CountOrderTransitionsAsync(
            OrderStatus status,
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
            => _context.OrderStatusHistories
                .AsNoTracking()
                .CountAsync(history => history.ToStatus == status
                    && history.CreatedAt >= from
                    && history.CreatedAt < to, cancellationToken);

        public async Task<IReadOnlyList<StatusSummary<PaymentStatus>>> GetPaymentStatusSummaryAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            var rows = await _context.Payments
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
            return rows
                .Select(row => new StatusSummary<PaymentStatus>(
                    row.Status,
                    row.Count,
                    row.Amount))
                .ToList();
        }

        public async Task<decimal> GetGrossPaidAmountAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
            => await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.PaidAt.HasValue
                    && payment.PaidAt.Value >= from
                    && payment.PaidAt.Value < to
                    && (payment.Status == PaymentStatus.Paid
                        || payment.Status == PaymentStatus.Refunded))
                .SumAsync(payment => (decimal?)payment.Amount, cancellationToken) ?? 0;

        public async Task<decimal> GetRefundedAmountAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
            => await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && history.OccurredAt >= from
                    && history.OccurredAt < to)
                .SumAsync(history => (decimal?)history.Payment!.Amount, cancellationToken) ?? 0;

        public Task<int> CountLowStockProductsAsync(
            int threshold,
            CancellationToken cancellationToken = default)
            => _context.Products
                .AsNoTracking()
                .CountAsync(product => !product.IsDeleted
                    && product.StockQuantity <= threshold,
                    cancellationToken);

        public async Task<IReadOnlyList<TopSellingProductResponse>> GetTopSellingProductsAsync(
            DateTime from,
            DateTime to,
            int limit,
            CancellationToken cancellationToken = default)
        {
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

            return await deliveredDetails
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
                .Take(limit)
                .ToListAsync(cancellationToken);
        }
    }
}
