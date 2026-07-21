using System.Data;
using AutoMapper;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ECommerceBackend.Application.Services
{
    public class CartService : ICartService
    {
        private readonly IGenericRepository<Cart> _cartRepo;
        private readonly IGenericRepository<CartItem> _cartItemRepo;
        private readonly IGenericRepository<Product> _productRepo;
        private readonly IAppDbContext _context;
        private readonly IDataConsistencyService _consistency;
        private readonly IMapper _mapper;

        public CartService(
            IGenericRepository<Cart> cartRepo,
            IGenericRepository<CartItem> cartItemRepo,
            IGenericRepository<Product> productRepo,
            IAppDbContext context,
            IDataConsistencyService consistency,
            IMapper mapper)
        {
            _cartRepo = cartRepo;
            _cartItemRepo = cartItemRepo;
            _productRepo = productRepo;
            _context = context;
            _consistency = consistency;
            _mapper = mapper;
        }

        public async Task<CartResponse> GetCartAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            var cart = await GetOrCreateCartAsync(userId, cancellationToken);
            return _mapper.Map<CartResponse>(cart);
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
                var product = await _productRepo.Query()
                    .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == request.ProductId, cancellationToken)
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
                    await _cartItemRepo.AddAsync(new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = cart.Id,
                        ProductId = request.ProductId,
                        Quantity = request.Quantity,
                        UnitPrice = product.Price
                    });
                }

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw new ConflictException("Giỏ hàng vừa được cập nhật bởi thao tác khác. Vui lòng thử lại.", ex);
            }
            catch (DbUpdateException ex) when (_consistency.IsUniqueConstraintViolation(ex))
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
                    _cartItemRepo.Delete(item);
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

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
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

                _cartItemRepo.Delete(item);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
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
                    _cartItemRepo.Delete(item);

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
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
                    await _context.Entry(cart)
                        .Collection(c => c.CartItems)
                        .Query()
                        .Include(ci => ci.Product)
                            .ThenInclude(p => p!.Images)
                        .LoadAsync(cancellationToken);
                }
            }
            else
            {
                cart = await _cartRepo.Query()
                    .Include(c => c.CartItems)
                        .ThenInclude(ci => ci.Product)
                            .ThenInclude(p => p!.Images)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);
            }

            if (cart != null)
                return cart;

            cart = new Cart { Id = Guid.NewGuid(), UserId = userId };
            await _cartRepo.AddAsync(cart);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (!lockForUpdate && _consistency.IsUniqueConstraintViolation(ex))
            {
                _context.Entry(cart).State = EntityState.Detached;

                var existingCart = await _cartRepo.Query()
                    .Include(c => c.CartItems)
                        .ThenInclude(ci => ci.Product)
                            .ThenInclude(p => p!.Images)
                    .AsSplitQuery()
                    .FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

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
                throw new BusinessException("cart_quantity_invalid", "Cart quantity must be greater than zero.");
        }

        private static void EnsureNonNegativeQuantity(int quantity)
        {
            if (quantity < 0)
                throw new BusinessException("cart_quantity_invalid", "Cart quantity cannot be negative.");
        }

    }
}
