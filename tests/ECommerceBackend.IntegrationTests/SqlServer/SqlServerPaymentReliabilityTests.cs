using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class SqlServerPaymentReliabilityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentOnlineRefunds_ReserveAmountAndCallProviderOnce()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = SqlServerIntegrationTestGate
            .CreateTestDatabaseConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var gateway = new BlockingRefundGateway();

        try
        {
            Guid orderId;
            Guid staffId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var fixture = await SeedReturnedCardOrderAsync(setupContext);
                orderId = fixture.OrderId;
                staffId = fixture.StaffId;
            }

            await using var firstContext = new AppDbContext(options);
            await using var secondContext = new AppDbContext(options);
            var firstUseCase = CreateUseCase(firstContext, gateway);
            var secondUseCase = CreateUseCase(secondContext, gateway);

            var firstTask = firstUseCase.ExecuteAsync(
                orderId,
                staffId,
                new RecordOrderRefundRequest
                {
                    Reference = "sql-refund-first",
                    Amount = 100m
                });
            await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var secondException = await Record.ExceptionAsync(() =>
                secondUseCase.ExecuteAsync(
                    orderId,
                    staffId,
                    new RecordOrderRefundRequest
                    {
                        Reference = "sql-refund-second",
                        Amount = 1m
                    })).WaitAsync(TimeSpan.FromSeconds(10));

            gateway.Release.TrySetResult(true);
            var firstResult = await firstTask.WaitAsync(
                TimeSpan.FromSeconds(10));

            var conflict = Assert.IsType<
                Application.Exceptions.ConflictException>(secondException);
            Assert.Equal("refund_amount_exceeds_available", conflict.Code);
            Assert.Equal(nameof(PaymentStatus.Refunded), firstResult.Payment?.Status);
            Assert.Equal(1, gateway.RefundCalls);

            await using var verificationContext = new AppDbContext(options);
            var payment = await verificationContext.Payments
                .AsNoTracking()
                .SingleAsync(item => item.OrderId == orderId);
            var refund = await verificationContext.PaymentRefunds
                .AsNoTracking()
                .SingleAsync(item => item.PaymentId == payment.Id);

            Assert.Equal(100m, payment.RefundedAmount);
            Assert.Equal(PaymentStatus.Refunded, payment.Status);
            Assert.Equal(PaymentRefundStatus.Succeeded, refund.Status);
            Assert.Equal("sql-refund-first", refund.IdempotencyKey);
        }
        finally
        {
            gateway.Release.TrySetResult(true);
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    private static OnlinePaymentRefundUseCase CreateUseCase(
        AppDbContext context,
        IPaymentGateway gateway)
    {
        var paymentRepository = new PaymentRepository(context);
        var orderRepository = new OrderRepository(context);
        return new OnlinePaymentRefundUseCase(
            gateway,
            paymentRepository,
            new FulfillmentRepository(context),
            orderRepository,
            context,
            TestServiceFactory.Consistency(context),
            new OutboxWriter(new OutboxRepository(context)),
            new OrderQueryUseCase(orderRepository),
            NullAuditWriter.Instance,
            new FixedTimeProvider(Now),
            Options.Create(new StripePaymentOptions
            {
                Enabled = true,
                SecretKey = "sk_test_sql_refund_secret_123456",
                PublishableKey = "pk_test_sql_refund_public_123456",
                WebhookSecret = "whsec_sql_refund_secret_123456",
                CreationLeaseSeconds = 120
            }));
    }

    private static async Task<(Guid OrderId, Guid StaffId)>
        SeedReturnedCardOrderAsync(AppDbContext context)
    {
        var customer = CreateUser("sql_refund_customer");
        var staff = CreateUser("sql_refund_staff");
        var order = Order.Create(
            Guid.NewGuid(),
            customer.Id,
            $"ORD-{Guid.NewGuid():N}"[..32],
            Guid.NewGuid().ToString("N"),
            new string('R', 64),
            null,
            null,
            ShippingMethod.Standard,
            "VND",
            Now.UtcDateTime.AddDays(-1),
            "1 SQL Refund Street",
            null);
        order.SetRecipient("SQL Refund Customer", "0900000000");
        order.SetPricing(100m, 0m, 0m, 0m);
        var payment = Payment.Create(
            Guid.NewGuid(),
            order.Id,
            PaymentMethod.Card,
            order.TotalAmount,
            "stripe",
            "pi_sql_refund_001",
            order.OrderDate,
            order.Currency);
        payment.ChangeStatus(
            PaymentStatus.Paid,
            order.OrderDate.AddHours(1));
        order.ChangeStatus(OrderStatus.Confirmed, payment.Status);
        order.ChangeStatus(OrderStatus.Shipping, payment.Status);
        order.ChangeStatus(OrderStatus.Delivered, payment.Status);
        order.ChangeStatus(OrderStatus.ReturnRequested, payment.Status);
        order.ChangeStatus(OrderStatus.ReturnApproved, payment.Status);
        order.ChangeStatus(OrderStatus.Returned, payment.Status);
        var returnRequest = ReturnRequest.Create(
            Guid.NewGuid(),
            order.Id,
            customer.Id,
            "Không còn phù hợp",
            order.OrderDate.AddHours(2));
        returnRequest.Review(
            ReturnReviewDecision.Approve,
            staff.Id,
            order.OrderDate.AddHours(3),
            null);
        returnRequest.Receive(
            staff.Id,
            order.OrderDate.AddHours(4),
            "Hàng hoàn hợp lệ");

        context.AddRange(customer, staff, order, payment, returnRequest);
        await context.SaveChangesAsync();
        return (order.Id, staff.Id);
    }

    private static User CreateUser(string userName)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FullName = userName,
            PasswordHash = "hash",
            CreatedAt = Now.UtcDateTime.AddDays(-2)
        };

    private sealed class BlockingRefundGateway : IPaymentGateway
    {
        private int _refundCalls;

        public string ProviderCode => "stripe";
        public int RefundCalls => Volatile.Read(ref _refundCalls);
        public TaskCompletionSource<bool> Started { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GatewayPaymentCreationResult> CreatePaymentAsync(
            GatewayPaymentCreationRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<GatewayPaymentStatusResult> GetPaymentAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async Task<GatewayRefundResult> RefundAsync(
            GatewayRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refundCalls);
            Started.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new GatewayRefundResult(
                "re_sql_refund_001",
                request.Amount,
                GatewayRefundStatus.Succeeded);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
