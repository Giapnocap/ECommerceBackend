using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Policies;

namespace ECommerceBackend.Application.Services
{
    public sealed class CheckoutCartLoader
    {
        private readonly ICartRepository _cartRepository;
        private readonly IDataConsistencyService _consistency;

        public CheckoutCartLoader(
            ICartRepository cartRepository,
            IDataConsistencyService consistency)
        {
            _cartRepository = cartRepository;
            _consistency = consistency;
        }

        internal async Task<Cart> LockAsync(
            Guid userId,
            CancellationToken cancellationToken)
            => await _consistency.LockCartByUserIdAsync(
                userId,
                cancellationToken)
                ?? throw new BusinessException(
                    "Không tìm thấy giỏ hàng.");

        internal async Task LoadItemsAsync(
            Cart cart,
            CancellationToken cancellationToken)
        {
            var productIds = await _cartRepository.GetProductIdsAsync(
                cart.Id,
                cancellationToken);
            if (productIds.Count == 0)
                throw EmptyCart();

            var products = await LoadProductsForUpdateAsync(
                productIds,
                cancellationToken);
            await _cartRepository.LoadItemsAsync(
                cart,
                cancellationToken);
            if (cart.CartItems.Count == 0)
                throw EmptyCart();

            foreach (var item in cart.CartItems)
            {
                if (!products.TryGetValue(
                    item.ProductId,
                    out var product))
                {
                    throw new BusinessException(
                        "Dữ liệu sản phẩm trong giỏ hàng "
                        + "không còn khả dụng.");
                }

                item.Product = product;
                DomainRuleGuard.AsBusiness(() =>
                    InventoryPolicy.EnsureCanReserve(
                        product,
                        item.Quantity));
            }
        }

        private async Task<Dictionary<Guid, Product>>
            LoadProductsForUpdateAsync(
                IEnumerable<Guid> productIds,
                CancellationToken cancellationToken)
        {
            var products = new Dictionary<Guid, Product>();
            foreach (var productId in productIds
                .Distinct()
                .OrderBy(id => id))
            {
                var product = await _consistency.LockProductAsync(
                    productId,
                    activeOnly: false,
                    cancellationToken)
                    ?? throw new BusinessException(
                        "Dữ liệu sản phẩm của giỏ hàng hoặc "
                        + "đơn hàng không còn tồn tại.");
                products.Add(productId, product);
            }

            return products;
        }

        private static BusinessException EmptyCart()
            => new(
                "Giỏ hàng trống. Vui lòng thêm sản phẩm "
                + "trước khi đặt hàng.");
    }
}
