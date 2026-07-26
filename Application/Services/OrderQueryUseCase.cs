using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public sealed class OrderQueryUseCase
    {
        private readonly IAppDbContext _context;

        public OrderQueryUseCase(IAppDbContext context)
        {
            _context = context;
        }

        public async Task<PagedResult<OrderResponse>> GetMyOrdersAsync(
            Guid userId,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(page, pageSize);
            var query = BuildQuery()
                .Where(order => order.UserId == userId)
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .ToListAsync(cancellationToken);
            return PagedResult<OrderResponse>.Create(
                items.Select(order => order.ToResponse()),
                totalCount,
                paging.Page,
                paging.Size);
        }

        public async Task<OrderResponse> GetByIdAsync(
            Guid orderId,
            Guid userId,
            bool canProcessOrders,
            CancellationToken cancellationToken = default)
        {
            var query = BuildQuery().Where(order => order.Id == orderId);
            if (!canProcessOrders)
                query = query.Where(order => order.UserId == userId);
            var order = await query.FirstOrDefaultAsync(cancellationToken)
                ?? throw new NotFoundException("Không tìm thấy đơn hàng.");
            return order.ToResponse();
        }

        public async Task<PagedResult<OrderResponse>> GetAllOrdersAsync(
            OrderQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            var paging = Paging.Normalize(
                queryParams.Page,
                queryParams.PageSize);
            var query = BuildQuery();
            if (queryParams.Status.HasValue)
            {
                query = query.Where(
                    order => order.Status == queryParams.Status.Value);
            }
            if (queryParams.UserId.HasValue)
            {
                query = query.Where(
                    order => order.UserId == queryParams.UserId.Value);
            }

            var orderedQuery = query
                .OrderByDescending(order => order.OrderDate)
                .ThenByDescending(order => order.Id);
            var totalCount = await orderedQuery.CountAsync(cancellationToken);
            var items = await orderedQuery
                .Skip(Paging.GetSkipCount(paging))
                .Take(paging.Size)
                .ToListAsync(cancellationToken);
            return PagedResult<OrderResponse>.Create(
                items.Select(order => order.ToResponse()),
                totalCount,
                paging.Page,
                paging.Size);
        }

        private IQueryable<Order> BuildQuery()
            => _context.Orders
                .AsNoTracking()
                .Include(order => order.OrderDetails)
                .Include(order => order.Payment)
                    .ThenInclude(payment =>
                        payment!.StatusHistory.OrderBy(
                            history => history.CreatedAt))
                .Include(order =>
                    order.StatusHistory.OrderBy(
                        history => history.CreatedAt))
                .AsSplitQuery();
    }
}
