using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
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
                    Amount = group.Sum(order => order.BaseTotalAmount)
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
                    Amount = group.Sum(
                        payment => payment.Order!.BaseTotalAmount)
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
                        || payment.Status == PaymentStatus.Refunded
                        || payment.Status == PaymentStatus.PartiallyRefunded))
                .SumAsync(
                    payment => (decimal?)payment.Order!.BaseTotalAmount,
                    cancellationToken) ?? 0;

        public async Task<decimal> GetRefundedAmountAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            var onlineRefunds = await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.Status == PaymentRefundStatus.Succeeded
                    && refund.CompletedAt.HasValue
                    && refund.CompletedAt.Value >= from
                    && refund.CompletedAt.Value < to)
                .SumAsync(
                    refund => (decimal?)refund.BaseAmount,
                    cancellationToken) ?? 0;
            var manualRefunds = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && history.Source == PaymentStatusChangeSource.ManualRefund
                    && history.OccurredAt >= from
                    && history.OccurredAt < to)
                .SumAsync(
                    history => (decimal?)history.Payment!.Order!.BaseTotalAmount,
                    cancellationToken) ?? 0;
            var externalFullRefunds = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && (history.Source == PaymentStatusChangeSource.Webhook
                        || history.Source == PaymentStatusChangeSource.Reconciliation)
                    && history.OccurredAt >= from
                    && history.OccurredAt < to
                    && !_context.PaymentRefunds.Any(refund =>
                        refund.PaymentId == history.PaymentId
                        && refund.Status == PaymentRefundStatus.Succeeded))
                .SumAsync(
                    history => (decimal?)history.Payment!.Order!.BaseTotalAmount,
                    cancellationToken) ?? 0;

            return onlineRefunds + manualRefunds + externalFullRefunds;
        }

        public async Task<IReadOnlyList<RevenueDailyAggregate>> GetRevenueDailyAggregatesAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            var grossRows = await _context.Payments
                .AsNoTracking()
                .Where(payment => payment.PaidAt.HasValue
                    && payment.PaidAt.Value >= from
                    && payment.PaidAt.Value < to
                    && (payment.Status == PaymentStatus.Paid
                        || payment.Status == PaymentStatus.Refunded
                        || payment.Status == PaymentStatus.PartiallyRefunded))
                .GroupBy(payment => payment.PaidAt!.Value.Date)
                .Select(group => new
                {
                    OccurredOn = group.Key,
                    GrossRevenue = group.Sum(
                        payment => payment.Order!.BaseTotalAmount),
                    OrderCount = group.Select(payment => payment.OrderId).Distinct().Count()
                })
                .ToListAsync(cancellationToken);
            var refundRows = await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.Status == PaymentRefundStatus.Succeeded
                    && refund.CompletedAt.HasValue
                    && refund.CompletedAt.Value >= from
                    && refund.CompletedAt.Value < to)
                .GroupBy(refund => refund.CompletedAt!.Value.Date)
                .Select(group => new
                {
                    OccurredOn = group.Key,
                    RefundAmount = group.Sum(
                        refund => refund.BaseAmount)
                })
                .ToListAsync(cancellationToken);
            var manualRefundRows = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && history.Source == PaymentStatusChangeSource.ManualRefund
                    && history.OccurredAt >= from
                    && history.OccurredAt < to)
                .GroupBy(history => history.OccurredAt.Date)
                .Select(group => new
                {
                    OccurredOn = group.Key,
                    RefundAmount = group.Sum(
                        history => history.Payment!.Order!.BaseTotalAmount)
                })
                .ToListAsync(cancellationToken);
            var externalFullRefundRows = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && (history.Source == PaymentStatusChangeSource.Webhook
                        || history.Source == PaymentStatusChangeSource.Reconciliation)
                    && history.OccurredAt >= from
                    && history.OccurredAt < to
                    && !_context.PaymentRefunds.Any(refund =>
                        refund.PaymentId == history.PaymentId
                        && refund.Status == PaymentRefundStatus.Succeeded))
                .GroupBy(history => history.OccurredAt.Date)
                .Select(group => new
                {
                    OccurredOn = group.Key,
                    RefundAmount = group.Sum(
                        history => history.Payment!.Order!.BaseTotalAmount)
                })
                .ToListAsync(cancellationToken);
            var aggregates = new Dictionary<DateTime, RevenueDailyAggregate>();

            foreach (var row in grossRows)
            {
                var day = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc);
                aggregates[day] = new RevenueDailyAggregate
                {
                    OccurredOn = day,
                    GrossRevenue = row.GrossRevenue,
                    OrderCount = row.OrderCount
                };
            }

            foreach (var row in refundRows)
            {
                var day = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc);
                if (!aggregates.TryGetValue(day, out var aggregate))
                {
                    aggregate = new RevenueDailyAggregate { OccurredOn = day };
                    aggregates[day] = aggregate;
                }

                aggregate.RefundAmount += row.RefundAmount;
            }

            foreach (var row in manualRefundRows)
            {
                var day = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc);
                if (!aggregates.TryGetValue(day, out var aggregate))
                {
                    aggregate = new RevenueDailyAggregate { OccurredOn = day };
                    aggregates[day] = aggregate;
                }

                aggregate.RefundAmount += row.RefundAmount;
            }

            foreach (var row in externalFullRefundRows)
            {
                var day = DateTime.SpecifyKind(row.OccurredOn, DateTimeKind.Utc);
                if (!aggregates.TryGetValue(day, out var aggregate))
                {
                    aggregate = new RevenueDailyAggregate { OccurredOn = day };
                    aggregates[day] = aggregate;
                }

                aggregate.RefundAmount += row.RefundAmount;
            }

            return aggregates.Values
                .OrderBy(row => row.OccurredOn)
                .ToList();
        }

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
                    detail.BaseUnitPrice,
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
                    Revenue = group.Sum(
                        detail => detail.BaseUnitPrice * detail.Quantity)
                })
                .OrderByDescending(product => product.QuantitySold)
                .ThenByDescending(product => product.Revenue)
                .ThenBy(product => product.ProductId)
                .Take(limit)
                .ToListAsync(cancellationToken);
        }

        public Task<int> CountNewCustomersAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
            => CustomerQuery().CountAsync(
                customer => customer.CreatedAt >= from && customer.CreatedAt < to,
                cancellationToken);

        public async Task<CustomerOrderReportMetrics> GetCustomerOrderMetricsAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
        {
            var rangeFrom = from;
            var rangeTo = to;
            var customerOrders =
                from order in _context.Orders.AsNoTracking()
                join customer in CustomerQuery() on order.UserId equals customer.Id
                where order.OrderDate >= rangeFrom && order.OrderDate < rangeTo
                select order;
            var row = await customerOrders
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    OrderCount = group.Count(),
                    CustomersWithOrdersCount = group
                        .Select(order => order.UserId)
                        .Distinct()
                        .Count()
                })
                .SingleOrDefaultAsync(cancellationToken);

            return new CustomerOrderReportMetrics
            {
                OrderCount = row?.OrderCount ?? 0,
                CustomersWithOrdersCount = row?.CustomersWithOrdersCount ?? 0
            };
        }

        public async Task<IReadOnlyList<TopCustomerResponse>> GetTopCustomersAsync(
            DateTime from,
            DateTime to,
            int limit,
            CancellationToken cancellationToken = default)
        {
            var rangeFrom = from;
            var rangeTo = to;
            var paidCustomers =
                from payment in _context.Payments.AsNoTracking()
                join order in _context.Orders.AsNoTracking()
                    on payment.OrderId equals order.Id
                join customer in CustomerQuery()
                    on order.UserId equals customer.Id
                where payment.PaidAt.HasValue
                    && payment.PaidAt.Value >= rangeFrom
                    && payment.PaidAt.Value < rangeTo
                    && (payment.Status == PaymentStatus.Paid
                        || payment.Status == PaymentStatus.Refunded
                        || payment.Status == PaymentStatus.PartiallyRefunded)
                group payment by new
                {
                    CustomerId = customer.Id,
                    customer.FullName,
                    customer.Email
                }
                into customerGroup
                select new
                {
                    customerGroup.Key.CustomerId,
                    customerGroup.Key.FullName,
                    customerGroup.Key.Email,
                    GrossAmount = customerGroup.Sum(
                        payment => payment.Order!.BaseTotalAmount),
                    OrderCount = customerGroup.Select(payment => payment.OrderId).Distinct().Count(),
                    RefundAmount = ((
                        from refund in _context.PaymentRefunds.AsNoTracking()
                        join refundedPayment in _context.Payments.AsNoTracking()
                            on refund.PaymentId equals refundedPayment.Id
                        join refundedOrder in _context.Orders.AsNoTracking()
                            on refundedPayment.OrderId equals refundedOrder.Id
                        where refund.Status == PaymentRefundStatus.Succeeded
                            && refund.CompletedAt.HasValue
                            && refund.CompletedAt.Value >= rangeFrom
                            && refund.CompletedAt.Value < rangeTo
                            && refundedOrder.UserId == customerGroup.Key.CustomerId
                        select (decimal?)refund.BaseAmount).Sum() ?? 0)
                    + ((
                        from history in _context.PaymentStatusHistories.AsNoTracking()
                        join refundedPayment in _context.Payments.AsNoTracking()
                            on history.PaymentId equals refundedPayment.Id
                        join refundedOrder in _context.Orders.AsNoTracking()
                            on refundedPayment.OrderId equals refundedOrder.Id
                        where history.ToStatus == PaymentStatus.Refunded
                            && history.Source == PaymentStatusChangeSource.ManualRefund
                            && history.OccurredAt >= rangeFrom
                            && history.OccurredAt < rangeTo
                            && refundedOrder.UserId == customerGroup.Key.CustomerId
                        select (decimal?)refundedOrder.BaseTotalAmount).Sum() ?? 0)
                    + ((
                        from history in _context.PaymentStatusHistories.AsNoTracking()
                        join refundedPayment in _context.Payments.AsNoTracking()
                            on history.PaymentId equals refundedPayment.Id
                        join refundedOrder in _context.Orders.AsNoTracking()
                            on refundedPayment.OrderId equals refundedOrder.Id
                        where history.ToStatus == PaymentStatus.Refunded
                            && (history.Source == PaymentStatusChangeSource.Webhook
                                || history.Source == PaymentStatusChangeSource.Reconciliation)
                            && history.OccurredAt >= rangeFrom
                            && history.OccurredAt < rangeTo
                            && refundedOrder.UserId == customerGroup.Key.CustomerId
                            && !_context.PaymentRefunds.Any(refund =>
                                refund.PaymentId == history.PaymentId
                                && refund.Status == PaymentRefundStatus.Succeeded)
                        select (decimal?)refundedOrder.BaseTotalAmount).Sum() ?? 0)
                };
            var rows = await paidCustomers
                .OrderByDescending(customer => customer.GrossAmount - customer.RefundAmount)
                .ThenBy(customer => customer.FullName)
                .ThenBy(customer => customer.CustomerId)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return rows
                .Select(customer => new TopCustomerResponse
                {
                    CustomerId = customer.CustomerId,
                    FullName = customer.FullName,
                    Email = customer.Email,
                    OrderCount = customer.OrderCount,
                    TotalSpent = customer.GrossAmount - customer.RefundAmount
                })
                .ToList();
        }

        public Task<int> CountReturnRequestsAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default)
            => _context.ReturnRequests
                .AsNoTracking()
                .CountAsync(
                    request => request.RequestedAt >= from && request.RequestedAt < to,
                    cancellationToken);

        public async Task<IReadOnlyList<ReturnReasonResponse>> GetCommonReturnReasonsAsync(
            DateTime from,
            DateTime to,
            int limit,
            CancellationToken cancellationToken = default)
            => await _context.ReturnRequests
                .AsNoTracking()
                .Where(request => request.RequestedAt >= from && request.RequestedAt < to)
                .GroupBy(request => request.Reason)
                .Select(group => new ReturnReasonResponse
                {
                    Reason = group.Key,
                    Count = group.Count()
                })
                .OrderByDescending(reason => reason.Count)
                .ThenBy(reason => reason.Reason)
                .Take(limit)
                .ToListAsync(cancellationToken);

        private IQueryable<User> CustomerQuery()
            => _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted
                    && user.UserRoles.Any(assignment => assignment.Role != null
                        && assignment.Role.Name == RoleNames.Customer));
    }
}
