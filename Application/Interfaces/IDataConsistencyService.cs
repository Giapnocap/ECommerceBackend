using System.Data;
using ECommerceBackend.Domain.Entities;

namespace ECommerceBackend.Application.Interfaces
{
    public interface IDataConsistencyService
    {
        Task<IAppTransaction> BeginTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default);

        Task<bool> TryAcquireDataRetentionLockAsync(CancellationToken cancellationToken = default);

        Task<Cart?> LockCartByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Category?> LockCategoryAsync(Guid categoryId, bool activeOnly, CancellationToken cancellationToken = default);
        Task<Order?> LockOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
        Task<OutboxMessage?> LockOutboxMessageAsync(Guid messageId, CancellationToken cancellationToken = default);
        Task<Payment?> LockPaymentAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken = default);
        Task<Payment?> LockPaymentByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default);
        Task<Product?> LockProductAsync(Guid productId, bool activeOnly, CancellationToken cancellationToken = default);
        Task<RefreshToken?> LockRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken = default);
        Task<User?> LockUserAsync(Guid userId, bool activeOnly, CancellationToken cancellationToken = default);

        bool IsDeadlock(Exception exception);
        bool IsUniqueConstraintViolation(Exception exception);
    }
}
