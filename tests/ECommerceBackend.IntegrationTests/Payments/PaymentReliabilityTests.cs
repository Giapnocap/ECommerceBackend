using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Domain.Policies;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class PaymentReliabilityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExternalCreation_IsIdempotentAndRunsOutsideDatabaseTransaction()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: false);
        var gateway = new RecordingGateway(context)
        {
            CreationResult = new GatewayPaymentCreationResult(
                "pi_reliable_001",
                "pi_reliable_001_secret",
                PaymentStatus.RequiresAction)
        };
        var useCase = CreateExternalCreation(context, gateway);

        var first = await useCase.ExecuteAsync(
            fixture.Order.Id,
            fixture.Customer.Id,
            canProcessOrders: false);
        var replay = await useCase.ExecuteAsync(
            fixture.Order.Id,
            fixture.Customer.Id,
            canProcessOrders: false);

        Assert.Equal(first.ProviderPaymentId, replay.ProviderPaymentId);
        Assert.Equal(2, gateway.CreateCalls);
        Assert.All(gateway.ObservedActiveTransactions, Assert.False);
        var payment = await context.Payments.SingleAsync();
        Assert.Equal("pi_reliable_001", payment.ProviderTransactionId);
        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        Assert.Null(payment.ExternalCreationLeaseUntil);
        Assert.Single(await context.PaymentStatusHistories
            .Where(history => history.Source == PaymentStatusChangeSource.Gateway)
            .ToListAsync());
    }

    [Fact]
    public async Task ExternalCreation_RequestInterruptedAfterProviderSuccess_RetriesWithSameIdempotencyKey()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: false);
        var clock = new MutableTimeProvider(Now);
        using var interruptedRequest = new CancellationTokenSource();
        var gateway = new RecordingGateway(context)
        {
            CreationResult = new GatewayPaymentCreationResult(
                "pi_interrupted_001",
                "pi_interrupted_001_secret",
                PaymentStatus.RequiresAction),
            AfterCreate = callCount =>
            {
                if (callCount == 1)
                    interruptedRequest.Cancel();
            }
        };
        var useCase = CreateExternalCreation(context, gateway, clock);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            useCase.ExecuteAsync(
                fixture.Order.Id,
                fixture.Customer.Id,
                canProcessOrders: false,
                interruptedRequest.Token));

        context.ChangeTracker.Clear();
        var interruptedPayment = await context.Payments.SingleAsync();
        Assert.Null(interruptedPayment.ProviderTransactionId);
        Assert.NotNull(interruptedPayment.ExternalCreationLeaseUntil);

        clock.Advance(TimeSpan.FromSeconds(121));
        var recovered = await useCase.ExecuteAsync(
            fixture.Order.Id,
            fixture.Customer.Id,
            canProcessOrders: false);

        Assert.Equal("pi_interrupted_001", recovered.ProviderPaymentId);
        Assert.Equal(2, gateway.CreateCalls);
        Assert.Single(gateway.CreationIdempotencyKeys.Distinct(StringComparer.Ordinal));
        Assert.All(gateway.ObservedActiveTransactions, Assert.False);
        var payment = await context.Payments.SingleAsync();
        Assert.Equal("pi_interrupted_001", payment.ProviderTransactionId);
        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        Assert.Null(payment.ExternalCreationLeaseUntil);
    }

    [Fact]
    public async Task Reconciliation_RecoversSucceededPaymentWhenWebhookWasMissed()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: false);
        fixture.Payment.AttachProviderTransaction(
            "stripe",
            "pi_reconcile_001",
            PaymentStatus.Processing,
            fixture.Order.OrderDate.AddMinutes(1));
        await context.SaveChangesAsync();
        var gateway = new RecordingGateway(context)
        {
            PaymentStatusResult = new GatewayPaymentStatusResult(
                "pi_reconcile_001",
                100,
                "VND",
                PaymentStatus.Paid)
        };
        var useCase = CreateReconciliation(context, gateway);

        var result = await useCase.ExecuteBatchAsync();
        var replay = await useCase.ExecuteBatchAsync();

        Assert.Equal(new PaymentReconciliationBatchResult(1, 1, 0), result);
        Assert.Equal(new PaymentReconciliationBatchResult(0, 0, 0), replay);
        Assert.All(gateway.ObservedActiveTransactions, Assert.False);
        var payment = await context.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Paid, payment.Status);
        Assert.Equal(Now.UtcDateTime, payment.LastReconciledAt);
        Assert.Single(await context.PaymentStatusHistories
            .Where(history =>
                history.Source == PaymentStatusChangeSource.Reconciliation)
            .ToListAsync());
    }

    [Fact]
    public async Task Reconciliation_ProviderMismatchFailsWithoutMutationAndRemainsRetryable()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: false);
        fixture.Payment.AttachProviderTransaction(
            "stripe",
            "pi_reconcile_mismatch",
            PaymentStatus.Processing,
            fixture.Order.OrderDate.AddMinutes(1));
        await context.SaveChangesAsync();
        var gateway = new RecordingGateway(context)
        {
            PaymentStatusResult = new GatewayPaymentStatusResult(
                "pi_reconcile_mismatch",
                99,
                "VND",
                PaymentStatus.Paid)
        };
        var useCase = CreateReconciliation(context, gateway);

        var first = await useCase.ExecuteBatchAsync();
        var retry = await useCase.ExecuteBatchAsync();

        Assert.Equal(new PaymentReconciliationBatchResult(1, 0, 1), first);
        Assert.Equal(new PaymentReconciliationBatchResult(1, 0, 1), retry);
        Assert.All(gateway.ObservedActiveTransactions, Assert.False);
        var payment = await context.Payments.SingleAsync();
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Null(payment.LastReconciledAt);
        Assert.Empty(await context.PaymentStatusHistories
            .Where(history =>
                history.Source == PaymentStatusChangeSource.Reconciliation)
            .ToListAsync());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task OnlineRefund_IsIdempotentAndNeverExceedsPaidAmount()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: true);
        var gateway = new RecordingGateway(context);
        var refund = CreateOnlineRefund(context, gateway);
        var firstRequest = new RecordOrderRefundRequest
        {
            Reference = "refund-partial-001",
            Amount = 25,
            Note = "Hoàn tiền một phần"
        };

        var partial = await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            firstRequest);
        var replay = await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            firstRequest);

        Assert.Equal(nameof(PaymentStatus.PartiallyRefunded), partial.Payment?.Status);
        Assert.Equal(partial.Id, replay.Id);
        Assert.Equal(1, gateway.RefundCalls);
        Assert.All(gateway.ObservedActiveTransactions, Assert.False);
        Assert.Equal(25, (await context.Payments.SingleAsync()).RefundedAmount);
        Assert.Equal(OrderStatus.Returned, (await context.Orders.SingleAsync()).Status);

        var full = await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new RecordOrderRefundRequest
            {
                Reference = "refund-final-001",
                Amount = 75
            });

        Assert.Equal(nameof(PaymentStatus.Refunded), full.Payment?.Status);
        Assert.Equal(nameof(OrderStatus.Refunded), full.Status);
        Assert.Equal(100, (await context.Payments.SingleAsync()).RefundedAmount);
        Assert.Equal(2, gateway.RefundCalls);
        Assert.Equal(2, await context.PaymentRefunds.CountAsync());
        var refunds = await context.PaymentRefunds.ToListAsync();
        Assert.All(
            refunds,
            item =>
            {
                Assert.Equal(PaymentRefundStatus.Succeeded, item.Status);
                Assert.Equal("VND", item.BaseCurrency);
            });
        Assert.Equal(100m, refunds.Sum(item => item.BaseAmount));
    }

    [Fact]
    public async Task OnlineRefund_CompletesWhenWebhookStateWinsFinalizeRace()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: true);
        var gateway = new RecordingGateway(context)
        {
            ApplyRefundBeforeReturning = true
        };
        var refund = CreateOnlineRefund(context, gateway);

        var result = await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new RecordOrderRefundRequest
            {
                Reference = "refund-webhook-race",
                Amount = 100m
            });

        Assert.Equal(nameof(PaymentStatus.Refunded), result.Payment?.Status);
        Assert.Equal(nameof(OrderStatus.Refunded), result.Status);
        Assert.Equal(100m, (await context.Payments.SingleAsync()).RefundedAmount);
        Assert.Equal(
            PaymentRefundStatus.Succeeded,
            (await context.PaymentRefunds.SingleAsync()).Status);
        Assert.Equal(1, gateway.RefundCalls);
    }

    [Fact]
    public async Task OnlineRefund_ReservesPendingAmountAgainstAnotherReference()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(context, returned: true);
        var gateway = new RecordingGateway(context)
        {
            RefundResult = new GatewayRefundResult(
                "re_pending_001",
                100,
                GatewayRefundStatus.Pending)
        };
        var refund = CreateOnlineRefund(context, gateway);

        _ = await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new RecordOrderRefundRequest
            {
                Reference = "refund-pending-001",
                Amount = 100
            });
        var exception = await Assert.ThrowsAsync<Application.Exceptions.ConflictException>(
            () => refund.ExecuteAsync(
                fixture.Order.Id,
                fixture.Staff.Id,
                new RecordOrderRefundRequest
                {
                    Reference = "refund-other-001",
                    Amount = 1
                }));

        Assert.Equal("refund_amount_exceeds_available", exception.Code);
        Assert.Single(await context.PaymentRefunds.ToListAsync());
    }

    [Fact]
    public async Task OnlineRefund_UsesOriginalUsdCurrencyAndRejectsInvalidScale()
    {
        await using var context = TestAppDbContext.Create();
        var fixture = await SeedCardOrderAsync(
            context,
            returned: true,
            currency: "USD");
        var gateway = new RecordingGateway(context);
        var refund = CreateOnlineRefund(context, gateway);

        await refund.ExecuteAsync(
            fixture.Order.Id,
            fixture.Staff.Id,
            new RecordOrderRefundRequest
            {
                Reference = "refund-usd-valid",
                Amount = 25.50m
            });

        Assert.NotNull(gateway.LastRefundRequest);
        Assert.Equal("USD", gateway.LastRefundRequest!.Currency);
        Assert.Equal(25.50m, gateway.LastRefundRequest.Amount);
        var storedRefund = await context.PaymentRefunds.SingleAsync();
        Assert.Equal(637_500m, storedRefund.BaseAmount);
        Assert.Equal("VND", storedRefund.BaseCurrency);
        var invalid = await Assert.ThrowsAsync<
            Application.Exceptions.BusinessException>(() =>
                refund.ExecuteAsync(
                    fixture.Order.Id,
                    fixture.Staff.Id,
                    new RecordOrderRefundRequest
                    {
                        Reference = "refund-usd-invalid-scale",
                        Amount = 1.001m
                    }));

        Assert.Equal("money_invalid", invalid.Code);
        Assert.Equal(1, gateway.RefundCalls);
    }

    private static ExternalPaymentCreationUseCase CreateExternalCreation(
        Infrastructure.Data.AppDbContext context,
        IPaymentGateway gateway,
        TimeProvider? timeProvider = null)
        => new(
            gateway,
            new PaymentRepository(context),
            TestServiceFactory.Consistency(context),
            context,
            NullAuditWriter.Instance,
            timeProvider ?? new FixedTimeProvider(Now),
            Options.Create(StripeOptions()));

    private static OnlinePaymentRefundUseCase CreateOnlineRefund(
        Infrastructure.Data.AppDbContext context,
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
            Options.Create(StripeOptions()));
    }

    private static PaymentReconciliationUseCase CreateReconciliation(
        Infrastructure.Data.AppDbContext context,
        IPaymentGateway gateway)
        => new(
            gateway,
            new PaymentRepository(context),
            TestServiceFactory.Consistency(context),
            context,
            new OutboxWriter(new OutboxRepository(context)),
            NullAuditWriter.Instance,
            new FixedTimeProvider(Now),
            Options.Create(StripeOptions()),
            NullLogger<PaymentReconciliationUseCase>.Instance);

    private static StripePaymentOptions StripeOptions()
        => new()
        {
            Enabled = true,
            SecretKey = "sk_test_reliability_secret_123456",
            PublishableKey = "pk_test_reliability_public_123456",
            WebhookSecret = "whsec_reliability_secret_123456",
            CreationLeaseSeconds = 120
        };

    private static async Task<CardOrderFixture> SeedCardOrderAsync(
        Infrastructure.Data.AppDbContext context,
        bool returned,
        string currency = "VND")
    {
        var customer = CreateUser("card_customer");
        var staff = CreateUser("card_staff");
        var order = Order.Create(
            Guid.NewGuid(),
            customer.Id,
            $"ORD-{Guid.NewGuid():N}"[..32],
            Guid.NewGuid().ToString("N"),
            new string('A', 64),
            null,
            null,
            ShippingMethod.Standard,
            currency,
            Now.UtcDateTime.AddDays(-1),
            "1 Payment Street",
            null);
        order.SetRecipient("Card Customer", "0900000000");
        if (string.Equals(currency, "VND", StringComparison.Ordinal))
        {
            order.SetPricing(100, 0, 0, 0);
        }
        else
        {
            order.SetPricingSnapshot(
                "VND",
                0.00004m,
                order.OrderDate,
                new OrderAmounts(2_500_000m, 0, 0, 0, 2_500_000m),
                new OrderAmounts(100m, 0, 0, 0, 100m));
        }
        var payment = Payment.Create(
            Guid.NewGuid(),
            order.Id,
            PaymentMethod.Card,
            order.TotalAmount,
            "stripe",
            returned ? "pi_refund_001" : null,
            order.OrderDate,
            order.Currency);

        ReturnRequest? returnRequest = null;
        if (returned)
        {
            payment.ChangeStatus(
                PaymentStatus.Paid,
                order.OrderDate.AddHours(1));
            order.ChangeStatus(OrderStatus.Confirmed, payment.Status);
            order.ChangeStatus(OrderStatus.Shipping, payment.Status);
            order.ChangeStatus(OrderStatus.Delivered, payment.Status);
            order.ChangeStatus(OrderStatus.ReturnRequested, payment.Status);
            order.ChangeStatus(OrderStatus.ReturnApproved, payment.Status);
            order.ChangeStatus(OrderStatus.Returned, payment.Status);
            returnRequest = ReturnRequest.Create(
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
        }

        context.AddRange(customer, staff, order, payment);
        if (returnRequest != null)
            context.ReturnRequests.Add(returnRequest);
        await context.SaveChangesAsync();
        return new CardOrderFixture(customer, staff, order, payment);
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
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password@123"),
            CreatedAt = Now.UtcDateTime.AddDays(-30)
        };

    private sealed record CardOrderFixture(
        User Customer,
        User Staff,
        Order Order,
        Payment Payment);

    private sealed class RecordingGateway(
        Infrastructure.Data.AppDbContext context) : IPaymentGateway
    {
        public string ProviderCode => "stripe";
        public int CreateCalls { get; private set; }
        public int RefundCalls { get; private set; }
        public List<bool> ObservedActiveTransactions { get; } = [];
        public List<string> CreationIdempotencyKeys { get; } = [];
        public GatewayPaymentCreationResult CreationResult { get; set; } =
            new("pi_default", "pi_default_secret", PaymentStatus.Pending);
        public Action<int>? AfterCreate { get; set; }
        public GatewayRefundResult? RefundResult { get; set; }
        public GatewayRefundRequest? LastRefundRequest { get; private set; }
        public GatewayPaymentStatusResult? PaymentStatusResult { get; set; }
        public bool ApplyRefundBeforeReturning { get; set; }

        public Task<GatewayPaymentCreationResult> CreatePaymentAsync(
            GatewayPaymentCreationRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            CreationIdempotencyKeys.Add(request.IdempotencyKey);
            ObservedActiveTransactions.Add(
                context.Database.CurrentTransaction != null);
            AfterCreate?.Invoke(CreateCalls);
            return Task.FromResult(CreationResult);
        }

        public async Task<GatewayRefundResult> RefundAsync(
            GatewayRefundRequest request,
            CancellationToken cancellationToken = default)
        {
            RefundCalls++;
            LastRefundRequest = request;
            ObservedActiveTransactions.Add(
                context.Database.CurrentTransaction != null);
            if (ApplyRefundBeforeReturning)
            {
                var payment = await context.Payments.SingleAsync(
                    item => item.Id == request.PaymentId,
                    cancellationToken);
                payment.RecordRefund(request.Amount, Now.UtcDateTime);
                await context.SaveChangesAsync(cancellationToken);
            }

            return RefundResult
                ?? new GatewayRefundResult(
                    $"re_{RefundCalls}",
                    request.Amount,
                    GatewayRefundStatus.Succeeded);
        }

        public Task<GatewayPaymentStatusResult> GetPaymentAsync(
            string providerPaymentId,
            CancellationToken cancellationToken = default)
        {
            ObservedActiveTransactions.Add(
                context.Database.CurrentTransaction != null);
            return Task.FromResult(PaymentStatusResult
                ?? new GatewayPaymentStatusResult(
                    providerPaymentId,
                    100,
                    "VND",
                    PaymentStatus.Processing));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }
}
