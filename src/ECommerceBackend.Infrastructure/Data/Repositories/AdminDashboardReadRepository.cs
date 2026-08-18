using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class AdminDashboardReadRepository : IAdminDashboardReadRepository
    {
        private readonly AppDbContext _context;

        public AdminDashboardReadRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardSummaryMetrics> GetSummaryMetricsAsync(
            DateTime todayStart,
            DateTime todayEnd,
            DateTime monthStart,
            DateTime now,
            int lowStockThreshold,
            CancellationToken cancellationToken = default)
        {
            var orderMetrics = await _context.Orders
                .AsNoTracking()
                .GroupBy(_ => 1)
                .Select(group => new
                {
                    Total = group.Count(),
                    Today = group.Count(order => order.OrderDate >= todayStart
                        && order.OrderDate < todayEnd),
                    Pending = group.Count(order => order.Status == OrderStatus.Pending),
                    Completed = group.Count(order => order.Status == OrderStatus.Delivered),
                    Cancelled = group.Count(order => order.Status == OrderStatus.Cancelled)
                })
                .SingleOrDefaultAsync(cancellationToken);

            var openReturnRequestCount = await _context.ReturnRequests
                .AsNoTracking()
                .CountAsync(
                    request => request.Status == ReturnRequestStatus.Pending
                        || request.Status == ReturnRequestStatus.Approved,
                    cancellationToken);

            var customerQuery = _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted
                    && user.UserRoles.Any(assignment => assignment.Role != null
                        && assignment.Role.Name == RoleNames.Customer));
            var totalCustomerCount = await customerQuery.CountAsync(cancellationToken);
            var newCustomerCountThisMonth = await customerQuery.CountAsync(
                user => user.CreatedAt >= monthStart && user.CreatedAt <= now,
                cancellationToken);

            var lowStockProductCount = await _context.Products
                .AsNoTracking()
                .CountAsync(
                    product => !product.IsDeleted
                        && product.StockQuantity <= lowStockThreshold,
                    cancellationToken);

            var revenueToday = await GetNetRevenueAsync(
                todayStart,
                todayEnd,
                cancellationToken);
            var revenueThisMonth = await GetNetRevenueAsync(
                monthStart,
                now,
                cancellationToken);

            return new DashboardSummaryMetrics
            {
                RevenueToday = revenueToday,
                RevenueThisMonth = revenueThisMonth,
                OrdersToday = orderMetrics?.Today ?? 0,
                TotalOrders = orderMetrics?.Total ?? 0,
                PendingOrderCount = orderMetrics?.Pending ?? 0,
                CompletedOrderCount = orderMetrics?.Completed ?? 0,
                CancelledOrderCount = orderMetrics?.Cancelled ?? 0,
                OpenReturnRequestCount = openReturnRequestCount,
                TotalCustomerCount = totalCustomerCount,
                NewCustomerCountThisMonth = newCustomerCountThisMonth,
                LowStockProductCount = lowStockProductCount
            };
        }

        public async Task<IReadOnlyList<StatusSummary<OrderStatus>>> GetOrderStatusSummaryAsync(
            CancellationToken cancellationToken = default)
        {
            var rows = await _context.Orders
                .AsNoTracking()
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

        public async Task<IReadOnlyList<DashboardRecentActivityResponse>> GetRecentActivitiesAsync(
            int limitPerSource,
            CancellationToken cancellationToken = default)
        {
            var orderActivities = await _context.Orders
                .AsNoTracking()
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id)
                .Take(limitPerSource)
                .Select(order => new DashboardRecentActivityResponse
                {
                    Type = "order",
                    EntityId = order.Id,
                    ActorUserId = order.UserId,
                    Action = "order.created",
                    Reference = order.OrderNumber,
                    OccurredAt = order.OrderDate
                })
                .ToListAsync(cancellationToken);

            var returnActivities = await _context.ReturnRequests
                .AsNoTracking()
                .OrderByDescending(request => request.RequestedAt)
                .ThenByDescending(request => request.Id)
                .Take(limitPerSource)
                .Select(request => new DashboardRecentActivityResponse
                {
                    Type = "return_request",
                    EntityId = request.Id,
                    ActorUserId = request.RequestedByUserId,
                    Action = "return.requested",
                    Reference = request.Order != null ? request.Order.OrderNumber : null,
                    OccurredAt = request.RequestedAt
                })
                .ToListAsync(cancellationToken);

            var auditActivities = await _context.AuditEvents
                .AsNoTracking()
                .OrderByDescending(auditEvent => auditEvent.CreatedAt)
                .ThenByDescending(auditEvent => auditEvent.Id)
                .Take(limitPerSource)
                .Select(auditEvent => new DashboardRecentActivityResponse
                {
                    Type = "administrative_action",
                    EntityId = auditEvent.Id,
                    ActorUserId = auditEvent.ActorUserId,
                    Action = auditEvent.Action,
                    Reference = auditEvent.EntityType,
                    OccurredAt = auditEvent.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return orderActivities
                .Concat(returnActivities)
                .Concat(auditActivities)
                .ToList();
        }

        private async Task<decimal> GetNetRevenueAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken)
        {
            var grossRevenue = await _context.Payments
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
            var onlineRefundedAmount = await _context.PaymentRefunds
                .AsNoTracking()
                .Where(refund => refund.Status == PaymentRefundStatus.Succeeded
                    && refund.CompletedAt.HasValue
                    && refund.CompletedAt.Value >= from
                    && refund.CompletedAt.Value < to)
                .SumAsync(
                    refund => (decimal?)refund.BaseAmount,
                    cancellationToken) ?? 0;
            var manualRefundedAmount = await _context.PaymentStatusHistories
                .AsNoTracking()
                .Where(history => history.ToStatus == PaymentStatus.Refunded
                    && history.Source == PaymentStatusChangeSource.ManualRefund
                    && history.OccurredAt >= from
                    && history.OccurredAt < to)
                .SumAsync(
                    history => (decimal?)history.Payment!.Order!.BaseTotalAmount,
                    cancellationToken) ?? 0;
            var externalFullRefundedAmount = await _context.PaymentStatusHistories
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

            return grossRevenue
                - onlineRefundedAmount
                - manualRefundedAmount
                - externalFullRefundedAmount;
        }
    }
}
