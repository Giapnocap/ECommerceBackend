using System.Data;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ECommerceBackend.Infrastructure.Data
{
    public sealed class EfDataConsistencyService : IDataConsistencyService
    {
        private readonly AppDbContext _context;

        public EfDataConsistencyService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IAppTransaction> BeginTransactionAsync(
            IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default)
        {
            if (!_context.Database.IsRelational())
                return new NoOpAppTransaction();

            var transaction = await _context.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken);
            return new EfAppTransaction(transaction);
        }

        public async Task<bool> TryAcquireDataRetentionLockAsync(CancellationToken cancellationToken = default)
            => await TryAcquireTransactionLockAsync(
                "ECommerceBackend.DataRetention",
                cancellationToken);

        public async Task<bool> TryAcquireRoleAssignmentLockAsync(
            CancellationToken cancellationToken = default)
            => await TryAcquireTransactionLockAsync(
                "ECommerceBackend.RoleAssignment",
                cancellationToken);

        private async Task<bool> TryAcquireTransactionLockAsync(
            string resource,
            CancellationToken cancellationToken)
        {
            if (!_context.Database.IsSqlServer())
                return true;

            var currentTransaction = _context.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Data retention lock requires an active transaction.");
            var connection = _context.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.Transaction = currentTransaction.GetDbTransaction();
            command.CommandTimeout = 15;
            command.CommandText = """
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock
                    @Resource = @resource,
                    @LockMode = N'Exclusive',
                    @LockOwner = N'Transaction',
                    @LockTimeout = 10000;
                SELECT @result;
                """;
            var resourceParameter = command.CreateParameter();
            resourceParameter.ParameterName = "@resource";
            resourceParameter.Value = resource;
            command.Parameters.Add(resourceParameter);

            var result = Convert.ToInt32(
                await command.ExecuteScalarAsync(cancellationToken),
                System.Globalization.CultureInfo.InvariantCulture);
            return result >= 0;
        }

        public async Task<Cart?> LockCartByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.Carts
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Carts] WITH (UPDLOCK, ROWLOCK) WHERE [UserId] = {userId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Carts
                .SingleOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);
        }

        public async Task<Category?> LockCategoryAsync(
            Guid categoryId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return activeOnly
                    ? await _context.Categories
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Categories] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {categoryId} AND [IsDeleted] = 0")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await _context.Categories
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Categories] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {categoryId}")
                        .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Categories.SingleOrDefaultAsync(
                category => category.Id == categoryId && (!activeOnly || !category.IsDeleted),
                cancellationToken);
        }

        public async Task<Order?> LockOrderAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.Orders
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Orders] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {orderId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Orders
                .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        }

        public async Task<OutboxMessage?> LockOutboxMessageAsync(
            Guid messageId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.OutboxMessages
                    .FromSqlInterpolated(
                        $"SELECT * FROM [OutboxMessages] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {messageId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.OutboxMessages
                .SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
        }

        public async Task<Payment?> LockPaymentAsync(
            string provider,
            string providerTransactionId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.Payments
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Payments] WITH (UPDLOCK, ROWLOCK) WHERE [Provider] = {provider} AND [ProviderTransactionId] = {providerTransactionId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Payments.SingleOrDefaultAsync(
                payment => payment.Provider == provider
                    && payment.ProviderTransactionId == providerTransactionId,
                cancellationToken);
        }

        public async Task<Payment?> LockPaymentByOrderIdAsync(
            Guid orderId,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.Payments
                    .FromSqlInterpolated(
                        $"SELECT * FROM [Payments] WITH (UPDLOCK, ROWLOCK) WHERE [OrderId] = {orderId}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Payments
                .SingleOrDefaultAsync(payment => payment.OrderId == orderId, cancellationToken);
        }

        public async Task<Product?> LockProductAsync(
            Guid productId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return activeOnly
                    ? await _context.Products
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {productId} AND [IsDeleted] = 0")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await _context.Products
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Products] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {productId}")
                        .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Products.SingleOrDefaultAsync(
                product => product.Id == productId && (!activeOnly || !product.IsDeleted),
                cancellationToken);
        }

        public async Task<RefreshToken?> LockRefreshTokenAsync(
            string tokenHash,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return await _context.RefreshTokens
                    .FromSqlInterpolated(
                        $"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, ROWLOCK) WHERE [TokenHash] = {tokenHash}")
                    .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.RefreshTokens
                .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        }

        public async Task<User?> LockUserAsync(
            Guid userId,
            bool activeOnly,
            CancellationToken cancellationToken = default)
        {
            if (_context.Database.IsSqlServer())
            {
                return activeOnly
                    ? await _context.Users
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Users] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {userId} AND [IsDeleted] = 0")
                        .SingleOrDefaultAsync(cancellationToken)
                    : await _context.Users
                        .FromSqlInterpolated(
                            $"SELECT * FROM [Users] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {userId}")
                        .SingleOrDefaultAsync(cancellationToken);
            }

            return await _context.Users.SingleOrDefaultAsync(
                user => user.Id == userId && (!activeOnly || !user.IsDeleted),
                cancellationToken);
        }

        public bool IsConcurrencyConflict(Exception exception)
            => exception is DbUpdateConcurrencyException;

        public bool IsDeadlock(Exception exception)
            => exception is SqlException { Number: 1205 }
                || exception.InnerException != null && IsDeadlock(exception.InnerException);

        public bool IsUniqueConstraintViolation(Exception exception)
            => exception is DbUpdateException
                && ContainsUniqueConstraintSqlError(exception);

        private static bool ContainsUniqueConstraintSqlError(Exception exception)
            => exception is SqlException { Number: 2601 or 2627 }
                || exception.InnerException != null
                    && ContainsUniqueConstraintSqlError(exception.InnerException);

        private sealed class EfAppTransaction : IAppTransaction
        {
            private readonly IDbContextTransaction _transaction;

            public EfAppTransaction(IDbContextTransaction transaction)
            {
                _transaction = transaction;
            }

            public Task CommitAsync(CancellationToken cancellationToken = default)
                => _transaction.CommitAsync(cancellationToken);

            public Task RollbackAsync(CancellationToken cancellationToken = default)
                => _transaction.RollbackAsync(cancellationToken);

            public ValueTask DisposeAsync() => _transaction.DisposeAsync();
        }

        private sealed class NoOpAppTransaction : IAppTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public Task RollbackAsync(CancellationToken cancellationToken = default)
                => Task.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
