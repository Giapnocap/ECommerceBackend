using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces.Repositories
{
    public interface ICartRepository
    {
        Task<Cart?> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task LoadItemsWithProductsAsync(
            Cart cart,
            CancellationToken cancellationToken = default);

        Task LoadItemsAsync(
            Cart cart,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<Guid>> GetProductIdsAsync(
            Guid cartId,
            CancellationToken cancellationToken = default);

        Task AddAsync(
            Cart cart,
            CancellationToken cancellationToken = default);

        Task AddItemAsync(
            CartItem item,
            CancellationToken cancellationToken = default);

        void RemoveItem(CartItem item);
    }
}
