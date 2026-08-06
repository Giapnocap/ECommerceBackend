using ECommerceBackend.Application.DTOs;

namespace ECommerceBackend.Application.Interfaces
{
    public interface ICartService
    {
        Task<CartResponse> GetCartAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

        Task<CartResponse> AddItemAsync(
            Guid userId,
            AddToCartRequest request,
            CancellationToken cancellationToken = default);

        Task<CartResponse> UpdateItemAsync(
            Guid userId,
            Guid cartItemId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default);

        Task<CartResponse> RemoveItemAsync(
            Guid userId,
            Guid cartItemId,
            CancellationToken cancellationToken = default);

        Task ClearCartAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
    }
}
