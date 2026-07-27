using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Infrastructure.Data.Repositories
{
    public sealed class CartRepository : ICartRepository
    {
        private readonly AppDbContext _context;

        public CartRepository(AppDbContext context)
        {
            _context = context;
        }

        public Task<Cart?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => _context.Carts
                .Include(cart => cart.CartItems)
                    .ThenInclude(item => item.Product)
                        .ThenInclude(product => product!.Images)
                .AsSplitQuery()
                .FirstOrDefaultAsync(
                    cart => cart.UserId == userId,
                    cancellationToken);

        public Task LoadItemsWithProductsAsync(
            Cart cart,
            CancellationToken cancellationToken = default)
            => _context.Entry(cart)
                .Collection(candidate => candidate.CartItems)
                .Query()
                .Include(item => item.Product)
                    .ThenInclude(product => product!.Images)
                .LoadAsync(cancellationToken);

        public Task LoadItemsAsync(
            Cart cart,
            CancellationToken cancellationToken = default)
            => _context.Entry(cart)
                .Collection(candidate => candidate.CartItems)
                .LoadAsync(cancellationToken);

        public async Task<IReadOnlyList<Guid>> GetProductIdsAsync(
            Guid cartId,
            CancellationToken cancellationToken = default)
            => await _context.CartItems
                .AsNoTracking()
                .Where(item => item.CartId == cartId)
                .Select(item => item.ProductId)
                .ToListAsync(cancellationToken);

        public Task AddAsync(
            Cart cart,
            CancellationToken cancellationToken = default)
            => _context.Carts.AddAsync(cart, cancellationToken).AsTask();

        public Task AddItemAsync(
            CartItem item,
            CancellationToken cancellationToken = default)
            => _context.CartItems.AddAsync(item, cancellationToken).AsTask();

        public void RemoveItem(CartItem item)
            => _context.CartItems.Remove(item);

        public void Detach(Cart cart)
            => _context.Entry(cart).State = EntityState.Detached;
    }
}
