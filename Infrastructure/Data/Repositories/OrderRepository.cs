using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _context;

        public OrderRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<PageSlice<Order>> GetByUserAsync(
            Guid userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = BuildReadQuery()
                .Where(order => order.UserId == userId)
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<Order>(items, totalCount);
        }

        public Task<Order?> GetByIdAsync(
            Guid orderId,
            Guid? ownerUserId,
            CancellationToken cancellationToken = default)
        {
            var query = BuildReadQuery().Where(order => order.Id == orderId);
            if (ownerUserId.HasValue)
                query = query.Where(order => order.UserId == ownerUserId.Value);
            return query.FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<PageSlice<Order>> GetAllAsync(
            OrderStatus? status,
            Guid? userId,
            int skip,
            int take,
            CancellationToken cancellationToken = default)
        {
            var query = BuildReadQuery();
            if (status.HasValue)
                query = query.Where(order => order.Status == status.Value);
            if (userId.HasValue)
                query = query.Where(order => order.UserId == userId.Value);

            var orderedQuery = query
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await orderedQuery.CountAsync(cancellationToken);
            var items = await orderedQuery
                .Skip(skip)
                .Take(take)
                .ToListAsync(cancellationToken);
            return new PageSlice<Order>(items, totalCount);
        }

        public Task<Order?> FindByIdempotencyKeyAsync(
            Guid userId,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => _context.Orders
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    order => order.UserId == userId
                        && order.IdempotencyKey == idempotencyKey,
                    cancellationToken);

        public Task<int> CountPendingByUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _context.Orders.CountAsync(
                order => order.UserId == userId
                    && order.Status == OrderStatus.Pending,
                cancellationToken);

        public async Task<IReadOnlyList<Guid>> GetDuePendingOrderIdsAsync(
            DateTime asOf,
            int batchSize,
            CancellationToken cancellationToken = default)
            => await _context.Orders
                .AsNoTracking()
                .Where(order => order.Status == OrderStatus.Pending
                    && order.ExpiresAt != null
                    && order.ExpiresAt <= asOf)
                .OrderBy(order => order.ExpiresAt)
                .ThenBy(order => order.Id)
                .Select(order => order.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

        public Task LoadDetailsAsync(
            Order order,
            CancellationToken cancellationToken = default)
            => _context.Entry(order)
                .Collection(candidate => candidate.OrderDetails)
                .LoadAsync(cancellationToken);

        public void Add(Order order)
            => _context.Orders.Add(order);

        public void AddDetail(OrderDetail detail)
            => _context.OrderDetails.Add(detail);

        public void AddStatusHistory(OrderStatusHistory history)
            => _context.OrderStatusHistories.Add(history);

        private IQueryable<Order> BuildReadQuery()
            => _context.Orders
                .AsNoTracking()
                .Include(order => order.OrderDetails)
                .Include(order => order.Payment)
                    .ThenInclude(payment =>
                        payment!.StatusHistory.OrderBy(
                            history => history.CreatedAt))
                .Include(order => order.Shipment)
                .Include(order => order.ReturnRequest)
                .Include(order =>
                    order.StatusHistory.OrderBy(
                        history => history.CreatedAt))
                .AsSplitQuery();
    }
}
