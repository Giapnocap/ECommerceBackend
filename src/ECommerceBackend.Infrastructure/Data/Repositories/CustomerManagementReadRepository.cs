using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class CustomerManagementReadRepository
        : ICustomerManagementReadRepository
    {
        private readonly AppDbContext _context;

        public CustomerManagementReadRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<bool> CustomerExistsAsync(
            Guid customerId,
            CancellationToken cancellationToken = default)
            => CustomerQuery()
                .AnyAsync(customer => customer.Id == customerId, cancellationToken);

        public async Task<PageSlice<CustomerListItemResponse>> GetCustomersAsync(
            CustomerQueryParams queryParams,
            string? accountStatus,
            DateTime now,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var customers = CustomerQuery();
            if (!string.IsNullOrWhiteSpace(queryParams.Keyword))
            {
                var keyword = queryParams.Keyword.Trim();
                var normalizedEmail = keyword.ToUpperInvariant();
                customers = customers.Where(customer => customer.FullName.Contains(keyword)
                    || customer.NormalizedEmail.Contains(normalizedEmail));
            }

            if (queryParams.RegisteredFrom.HasValue)
            {
                customers = customers.Where(
                    customer => customer.CreatedAt >= queryParams.RegisteredFrom.Value);
            }

            if (queryParams.RegisteredTo.HasValue)
            {
                customers = customers.Where(
                    customer => customer.CreatedAt < queryParams.RegisteredTo.Value);
            }

            customers = accountStatus switch
            {
                "active" => customers.Where(customer => !customer.LockoutEndAt.HasValue
                    || customer.LockoutEndAt.Value <= now),
                "locked" => customers.Where(customer => customer.LockoutEndAt.HasValue
                    && customer.LockoutEndAt.Value > now),
                _ => customers
            };

            var projected = customers.Select(customer => new CustomerListItemResponse
            {
                CustomerId = customer.Id,
                FullName = customer.FullName,
                Email = customer.Email,
                AccountStatus = customer.LockoutEndAt.HasValue
                    && customer.LockoutEndAt.Value > now
                    ? "Locked"
                    : "Active",
                RegisteredAt = customer.CreatedAt,
                OrderCount = _context.Orders.Count(order => order.UserId == customer.Id),
                TotalSpent = (_context.Payments
                    .Where(payment => payment.Order != null
                        && payment.Order.UserId == customer.Id
                        && (payment.Status == PaymentStatus.Paid
                            || payment.Status == PaymentStatus.Refunded
                            || payment.Status == PaymentStatus.PartiallyRefunded))
                    .Sum(
                        payment => (decimal?)payment.Order!.BaseTotalAmount) ?? 0)
                    - (_context.PaymentRefunds
                        .Where(refund => refund.Status == PaymentRefundStatus.Succeeded
                            && refund.Payment != null
                            && refund.Payment.Order != null
                            && refund.Payment.Order.UserId == customer.Id)
                        .Sum(refund => (decimal?)refund.BaseAmount) ?? 0)
                    - (_context.PaymentStatusHistories
                        .Where(history => history.ToStatus == PaymentStatus.Refunded
                            && history.Source == PaymentStatusChangeSource.ManualRefund
                            && history.Payment != null
                            && history.Payment.Order != null
                            && history.Payment.Order.UserId == customer.Id)
                        .Sum(history =>
                            (decimal?)history.Payment!.Order!.BaseTotalAmount) ?? 0)
                    - (_context.PaymentStatusHistories
                        .Where(history => history.ToStatus == PaymentStatus.Refunded
                            && (history.Source == PaymentStatusChangeSource.Webhook
                                || history.Source == PaymentStatusChangeSource.Reconciliation)
                            && history.Payment != null
                            && history.Payment.Order != null
                            && history.Payment.Order.UserId == customer.Id
                            && !_context.PaymentRefunds.Any(refund =>
                                refund.PaymentId == history.PaymentId
                                && refund.Status == PaymentRefundStatus.Succeeded))
                        .Sum(history =>
                            (decimal?)history.Payment!.Order!.BaseTotalAmount) ?? 0)
            });

            var sorted = ApplySort(projected, queryParams);
            var totalCount = await sorted.CountAsync(cancellationToken);
            var items = await sorted
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<CustomerListItemResponse>(items, totalCount);
        }

        public Task<CustomerDetailResponse?> GetCustomerDetailAsync(
            Guid customerId,
            DateTime now,
            CancellationToken cancellationToken = default)
            => CustomerQuery()
                .Where(customer => customer.Id == customerId)
                .Select(customer => new CustomerDetailResponse
                {
                    CustomerId = customer.Id,
                    FullName = customer.FullName,
                    Email = customer.Email,
                    Phone = customer.Phone,
                    AccountStatus = customer.LockoutEndAt.HasValue
                        && customer.LockoutEndAt.Value > now
                        ? "Locked"
                        : "Active",
                    LockedUntil = customer.LockoutEndAt.HasValue
                        && customer.LockoutEndAt.Value > now
                        ? customer.LockoutEndAt
                        : null,
                    RegisteredAt = customer.CreatedAt,
                    TotalOrderCount = _context.Orders.Count(order => order.UserId == customer.Id),
                    CompletedOrderCount = _context.Orders.Count(order => order.UserId == customer.Id
                        && order.Status == OrderStatus.Delivered),
                    CancelledOrderCount = _context.Orders.Count(order => order.UserId == customer.Id
                        && order.Status == OrderStatus.Cancelled),
                    ReturnRequestCount = _context.ReturnRequests.Count(
                        request => request.RequestedByUserId == customer.Id),
                    TotalSpent = (_context.Payments
                        .Where(payment => payment.Order != null
                            && payment.Order.UserId == customer.Id
                            && (payment.Status == PaymentStatus.Paid
                                || payment.Status == PaymentStatus.Refunded
                                || payment.Status == PaymentStatus.PartiallyRefunded))
                        .Sum(payment =>
                            (decimal?)payment.Order!.BaseTotalAmount) ?? 0)
                        - (_context.PaymentRefunds
                            .Where(refund => refund.Status == PaymentRefundStatus.Succeeded
                                && refund.Payment != null
                                && refund.Payment.Order != null
                                && refund.Payment.Order.UserId == customer.Id)
                            .Sum(refund => (decimal?)refund.BaseAmount) ?? 0)
                        - (_context.PaymentStatusHistories
                            .Where(history => history.ToStatus == PaymentStatus.Refunded
                                && history.Source == PaymentStatusChangeSource.ManualRefund
                                && history.Payment != null
                                && history.Payment.Order != null
                                && history.Payment.Order.UserId == customer.Id)
                            .Sum(history =>
                                (decimal?)history.Payment!.Order!.BaseTotalAmount) ?? 0)
                        - (_context.PaymentStatusHistories
                            .Where(history => history.ToStatus == PaymentStatus.Refunded
                                && (history.Source == PaymentStatusChangeSource.Webhook
                                    || history.Source == PaymentStatusChangeSource.Reconciliation)
                                && history.Payment != null
                                && history.Payment.Order != null
                                && history.Payment.Order.UserId == customer.Id
                                && !_context.PaymentRefunds.Any(refund =>
                                    refund.PaymentId == history.PaymentId
                                    && refund.Status == PaymentRefundStatus.Succeeded))
                            .Sum(history =>
                                (decimal?)history.Payment!.Order!.BaseTotalAmount) ?? 0),
                    LastOrder = _context.Orders
                        .Where(order => order.UserId == customer.Id)
                        .OrderByDescending(order => order.OrderDate)
                        .ThenByDescending(order => order.Id)
                        .Select(order => new CustomerLastOrderResponse
                        {
                            OrderId = order.Id,
                            OrderNumber = order.OrderNumber,
                            Status = order.Status.ToString(),
                            TotalAmount = order.TotalAmount,
                            Currency = order.Currency,
                            OrderedAt = order.OrderDate
                        })
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken);

        public async Task<PageSlice<CustomerOrderResponse>> GetOrdersAsync(
            Guid customerId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var orders = _context.Orders
                .AsNoTracking()
                .Where(order => order.UserId == customerId)
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await orders.CountAsync(cancellationToken);
            var items = await orders
                .Skip(skip)
                .Take(take)
                .Select(order => new CustomerOrderResponse
                {
                    OrderId = order.Id,
                    OrderNumber = order.OrderNumber,
                    Status = order.Status.ToString(),
                    TotalAmount = order.TotalAmount,
                    Currency = order.Currency,
                    OrderedAt = order.OrderDate
                })
                .ToListAsync(cancellationToken);

            return new PageSlice<CustomerOrderResponse>(items, totalCount);
        }

        public async Task<PageSlice<CustomerReturnResponse>> GetReturnsAsync(
            Guid customerId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var returns = _context.ReturnRequests
                .AsNoTracking()
                .Where(request => request.RequestedByUserId == customerId)
                .OrderByDescending(request => request.RequestedAt)
                .ThenByDescending(request => request.Id);
            var totalCount = await returns.CountAsync(cancellationToken);
            var items = await returns
                .Skip(skip)
                .Take(take)
                .Select(request => new CustomerReturnResponse
                {
                    ReturnRequestId = request.Id,
                    OrderId = request.OrderId,
                    OrderNumber = request.Order != null ? request.Order.OrderNumber : string.Empty,
                    Status = request.Status.ToString(),
                    Reason = request.Reason,
                    RequestedAt = request.RequestedAt,
                    ReviewedAt = request.ReviewedAt,
                    ReceivedAt = request.ReceivedAt,
                    RefundedAt = request.RefundedAt
                })
                .ToListAsync(cancellationToken);

            return new PageSlice<CustomerReturnResponse>(items, totalCount);
        }

        private IQueryable<User> CustomerQuery()
            => _context.Users
                .AsNoTracking()
                .Where(user => !user.IsDeleted
                    && user.UserRoles.Any(assignment => assignment.Role != null
                        && assignment.Role.Name == RoleNames.Customer));

        private static IQueryable<CustomerListItemResponse> ApplySort(
            IQueryable<CustomerListItemResponse> query,
            CustomerQueryParams queryParams)
            => (queryParams.SortBy?.ToLowerInvariant(), queryParams.SortOrder?.ToLowerInvariant()) switch
            {
                ("name", "desc") => query.OrderByDescending(customer => customer.FullName)
                    .ThenBy(customer => customer.CustomerId),
                ("name", _) => query.OrderBy(customer => customer.FullName)
                    .ThenBy(customer => customer.CustomerId),
                ("orders", "asc") => query.OrderBy(customer => customer.OrderCount)
                    .ThenBy(customer => customer.CustomerId),
                ("orders", _) => query.OrderByDescending(customer => customer.OrderCount)
                    .ThenBy(customer => customer.CustomerId),
                ("spent", "asc") => query.OrderBy(customer => customer.TotalSpent)
                    .ThenBy(customer => customer.CustomerId),
                ("spent", _) => query.OrderByDescending(customer => customer.TotalSpent)
                    .ThenBy(customer => customer.CustomerId),
                ("registeredat", "asc") => query.OrderBy(customer => customer.RegisteredAt)
                    .ThenBy(customer => customer.CustomerId),
                _ => query.OrderByDescending(customer => customer.RegisteredAt)
                    .ThenByDescending(customer => customer.CustomerId)
            };
    }
}
