using System.Data;
using ECommerceBackend.Application.Common;
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
            DomainRuleGuard.AsBusiness(() =>
                CartItem.EnsurePositiveQuantity(request.Quantity));
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
                    DomainRuleGuard.AsBusiness(() =>
                        existingItem.IncreaseQuantity(
                            request.Quantity,
                            product));
                }
                else
                {
                    var item = DomainRuleGuard.AsBusiness(() =>
                        cart.AddItem(
                            Guid.NewGuid(),
                            product,
                            request.Quantity));
                    await _cartRepository.AddItemAsync(
                        item,
                        cancellationToken);
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
            DomainRuleGuard.AsBusiness(() =>
                CartItem.EnsureNonNegativeQuantity(request.Quantity));
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
                    DomainRuleGuard.AsBusiness(() =>
                        cart.RemoveItem(item));
                    _cartRepository.RemoveItem(item);
                }
                else
                {
                    var product = item.Product;
                    if (product == null)
                        throw new BusinessException("Sản phẩm đã ngừng bán. Vui lòng xóa sản phẩm khỏi giỏ hàng.");

                    DomainRuleGuard.AsBusiness(() =>
                        item.SetQuantity(
                            request.Quantity,
                            product));
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

                DomainRuleGuard.AsBusiness(() =>
                    cart.RemoveItem(item));
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
                foreach (var item in cart.ClearItems())
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

            cart = DomainRuleGuard.AsBusiness(() =>
                Cart.Create(Guid.NewGuid(), userId));
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

    }
}
