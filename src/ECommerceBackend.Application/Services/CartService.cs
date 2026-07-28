using System.Data;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Interfaces.Persistence;
using ECommerceBackend.Application.Interfaces.Repositories;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Services
{
    public class CartService : ICartService
    {
        private readonly ICartRepository _cartRepository;
        private readonly IProductRepository _productRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IDataConsistencyService _consistency;

        public CartService(
            ICartRepository cartRepository,
            IProductRepository productRepository,
            IUnitOfWork unitOfWork,
            IDataConsistencyService consistency)
        {
            _cartRepository = cartRepository;
            _productRepository = productRepository;
            _unitOfWork = unitOfWork;
            _consistency = consistency;
        }

        public async Task<CartResponse> GetCartAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var cart = await GetOrCreateCartAsync(userId, cancellationToken);
            return cart.ToResponse();
        }

        public async Task<CartResponse> AddItemAsync(
            Guid userId,
            AddToCartRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsurePositiveQuantity(request.Quantity);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var cart = await GetOrCreateCartAsync(userId, cancellationToken, lockForUpdate: true);
                var product = await _productRepository.GetActiveForCartAsync(
                    request.ProductId,
                    cancellationToken)
                    ?? throw new NotFoundException("Không tìm thấy sản phẩm.");

                var existingItem = cart.CartItems.FirstOrDefault(item => item.ProductId == request.ProductId);
                if (existingItem != null)
                {
                    var newQuantity = (long)existingItem.Quantity + request.Quantity;
                    EnsureStockAvailable(product, newQuantity);

                    existingItem.Quantity = (int)newQuantity;
                    existingItem.UnitPrice = product.Price;
                }
                else
                {
                    EnsureStockAvailable(product, request.Quantity);
                    await _cartRepository.AddItemAsync(new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = cart.Id,
                        ProductId = request.ProductId,
                        Quantity = request.Quantity,
                        UnitPrice = product.Price
                    }, cancellationToken);
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng vừa được cập nhật bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (Exception ex) when (_consistency.IsUniqueConstraintViolation(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Sản phẩm đã được thêm vào giỏ hàng bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng đang được xử lý bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return await GetCartAsync(userId, cancellationToken);
        }

        public async Task<CartResponse> UpdateItemAsync(
            Guid userId,
            Guid cartItemId,
            UpdateCartItemRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureNonNegativeQuantity(request.Quantity);
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var cart = await GetOrCreateCartAsync(userId, cancellationToken, lockForUpdate: true);
                var item = cart.CartItems.FirstOrDefault(cartItem => cartItem.Id == cartItemId)
                    ?? throw new NotFoundException("Không tìm thấy sản phẩm trong giỏ hàng.");

                if (request.Quantity == 0)
                {
                    _cartRepository.RemoveItem(item);
                }
                else
                {
                    var product = item.Product;
                    if (product == null || product.IsDeleted)
                        throw new BusinessException("Sản phẩm đã ngừng bán. Vui lòng xóa sản phẩm khỏi giỏ hàng.");

                    EnsureStockAvailable(product, request.Quantity);
                    item.Quantity = request.Quantity;
                    item.UnitPrice = product.Price;
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng vừa được cập nhật bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng đang được xử lý bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return await GetCartAsync(userId, cancellationToken);
        }

        public async Task<CartResponse> RemoveItemAsync(
            Guid userId,
            Guid cartItemId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var cart = await GetOrCreateCartAsync(userId, cancellationToken, lockForUpdate: true);
                var item = cart.CartItems.FirstOrDefault(cartItem => cartItem.Id == cartItemId)
                    ?? throw new NotFoundException("Không tìm thấy sản phẩm trong giỏ hàng.");

                _cartRepository.RemoveItem(item);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng vừa được cập nhật bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng đang được xử lý bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }

            return await GetCartAsync(userId, cancellationToken);
        }

        public async Task ClearCartAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            await using var transaction = await _consistency.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                var cart = await GetOrCreateCartAsync(userId, cancellationToken, lockForUpdate: true);
                foreach (var item in cart.CartItems.ToList())
                    _cartRepository.RemoveItem(item);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception ex) when (_consistency.IsConcurrencyConflict(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng vừa được cập nhật bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (Exception ex) when (_consistency.IsDeadlock(ex))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng đang được xử lý bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private async Task<Cart> GetOrCreateCartAsync(
            Guid userId,
            CancellationToken cancellationToken,
            bool lockForUpdate = false)
        {
            Cart? cart;
            if (lockForUpdate)
            {
                cart = await _consistency.LockCartByUserIdAsync(userId, cancellationToken);

                if (cart != null)
                {
                    await _cartRepository.LoadItemsWithProductsAsync(
                        cart,
                        cancellationToken);
                }
            }
            else
            {
                cart = await _cartRepository.GetByUserIdAsync(
                    userId,
                    cancellationToken);
            }

            if (cart != null)
                return cart;

            cart = new Cart { Id = Guid.NewGuid(), UserId = userId };
            await _cartRepository.AddAsync(cart, cancellationToken);
            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (!lockForUpdate && _consistency.IsUniqueConstraintViolation(ex))
            {
                _cartRepository.Detach(cart);

                var existingCart = await _cartRepository.GetByUserIdAsync(
                    userId,
                    cancellationToken);

                if (existingCart != null)
                    return existingCart;

                throw;
            }

            return cart;
        }

        private static void EnsureStockAvailable(Product product, long requestedQuantity)
        {
            if (requestedQuantity > product.StockQuantity)
            {
                throw new BusinessException(
                    $"Sản phẩm '{product.Name}' chỉ còn {product.StockQuantity} trong kho.");
            }
        }

        private static void EnsurePositiveQuantity(int quantity)
        {
            if (quantity <= 0)
                throw new BusinessException("cart_quantity_invalid", "Số lượng sản phẩm trong giỏ hàng phải lớn hơn 0.");
        }

        private static void EnsureNonNegativeQuantity(int quantity)
        {
            if (quantity < 0)
                throw new BusinessException("cart_quantity_invalid", "Số lượng sản phẩm trong giỏ hàng không được là số âm.");
        }

    }
}
