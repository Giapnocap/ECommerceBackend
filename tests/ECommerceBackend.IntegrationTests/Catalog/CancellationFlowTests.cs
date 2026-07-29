using System.Data;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;

namespace ECommerceBackend.Tests;

public sealed class CancellationFlowTests
{
    [Fact]
    public async Task CategoryCreate_WhenCancelledAfterTransactionStarts_RollsBackWithoutRequestToken()
    {
        await using var context = TestAppDbContext.Create();
        using var cancellation = new CancellationTokenSource();
        var consistency = new CancelAfterBeginConsistencyService(cancellation);
        var service = new CategoryService(
            new CategoryRepository(context),
            context,
            consistency);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.CreateAsync(
                new CreateCategoryRequest { Name = "Cancelled category" },
                cancellationToken: cancellation.Token));

        Assert.Equal(1, consistency.Transaction.RollbackCount);
        Assert.False(consistency.Transaction.RollbackToken.IsCancellationRequested);
        Assert.Equal(0, consistency.Transaction.CommitCount);
        Assert.Empty(context.Categories);
    }

    private sealed class CancelAfterBeginConsistencyService : IDataConsistencyService
    {
        private readonly CancellationTokenSource _cancellation;

        public CancelAfterBeginConsistencyService(CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
        }

        public RecordingTransaction Transaction { get; } = new();

        public Task<IAppTransaction> BeginTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default)
        {
            _cancellation.Cancel();
            return Task.FromResult<IAppTransaction>(Transaction);
        }

        public Task<bool> TryAcquireDataRetentionLockAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> TryAcquireRoleAssignmentLockAsync(
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Cart?> LockCartByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Category?> LockCategoryAsync(
            Guid categoryId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Order?> LockOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<OutboxMessage?> LockOutboxMessageAsync(
            Guid messageId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Payment?> LockPaymentAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Payment?> LockPaymentByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<Product?> LockProductAsync(
            Guid productId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<RefreshToken?> LockRefreshTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<User?> LockUserAsync(
            Guid userId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public bool IsConcurrencyConflict(Exception exception) => false;

        public bool IsDeadlock(Exception exception) => false;

        public bool IsUniqueConstraintViolation(Exception exception) => false;
    }

    private sealed class RecordingTransaction : IAppTransaction
    {
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public CancellationToken RollbackToken { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            RollbackToken = cancellationToken;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
