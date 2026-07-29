using System.Data;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ECommerceBackend.Application.Common;
using ECommerceBackend.Application.DTOs;
using ECommerceBackend.Application.Exceptions;
using ECommerceBackend.Application.Interfaces;
using ECommerceBackend.Application.Mappings;
using ECommerceBackend.Application.Services;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Domain.Enums;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Infrastructure.Data.Repositories;
using ECommerceBackend.Infrastructure.Notifications;
using ECommerceBackend.Infrastructure.Payments;
using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ECommerceBackend.Tests;

public sealed class SqlServerCommerceFlowTests
{
    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task CheckoutRetryCancellationAndDelivery_PreserveLifecycleAndInventoryInvariants()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            await context.Database.MigrateAsync();

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "integration_customer",
                NormalizedUserName = "INTEGRATION_CUSTOMER",
                Email = "integration_customer@example.com",
                NormalizedEmail = "INTEGRATION_CUSTOMER@EXAMPLE.COM",
                FullName = "Integration Customer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
                CreatedAt = DateTime.UtcNow
            };
            var cart = new Cart { Id = Guid.NewGuid(), UserId = user.Id };
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Integration",
                NormalizedName = "INTEGRATION"
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Name = "Integration Product",
                Price = 125.50m,
                StockQuantity = 5,
                Description = "SQL Server integration product",
                CreatedAt = DateTime.UtcNow
            };
            context.AddRange(user, cart, category, product);
            context.CartItems.Add(new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = 2,
                UnitPrice = product.Price
            });
            await context.SaveChangesAsync();

            var service = CreateOrderService(context);
            var request = new PlaceOrderRequest
            {
                ShippingAddress = "1 Integration Street",
                PaymentMethod = PaymentMethod.CashOnDelivery
            };
            var first = await service.PlaceOrderAsync(user.Id, request, "integration-checkout-1");
            var retry = await service.PlaceOrderAsync(user.Id, request, "integration-checkout-1");

            Assert.Equal(first.Id, retry.Id);
            Assert.Equal(nameof(OrderStatus.Pending), first.Status);
            Assert.NotNull(first.ExpiresAt);
            Assert.True(first.ExpiresAt > first.OrderDate);
            var initialHistory = Assert.Single(await context.OrderStatusHistories
                .AsNoTracking()
                .Where(history => history.OrderId == first.Id)
                .ToListAsync());
            Assert.Null(initialHistory.FromStatus);
            Assert.Equal(OrderStatus.Pending, initialHistory.ToStatus);
            Assert.Equal(3, await context.Products
                .Where(item => item.Id == product.Id)
                .Select(item => item.StockQuantity)
                .SingleAsync());
            Assert.Equal("Integration Product", Assert.Single(first.OrderDetails).ProductName);
            Assert.Equal(nameof(PaymentStatus.Pending), first.Payment?.Status);
            Assert.Equal("cod", first.Payment?.Provider);
            var initialPaymentHistory = Assert.Single(first.Payment!.StatusHistory);
            Assert.Null(initialPaymentHistory.FromStatus);
            Assert.Equal(nameof(PaymentStatus.Pending), initialPaymentHistory.ToStatus);
            Assert.Equal(nameof(PaymentStatusChangeSource.Checkout), initialPaymentHistory.Source);
            Assert.Single(await context.InventoryTransactions
                .Where(item => item.OrderId == first.Id
                    && item.Type == InventoryTransactionType.OrderPlaced)
                .ToListAsync());

            var cancelled = await service.CancelByCustomerAsync(
                first.Id,
                user.Id,
                new CancelOrderRequest { Reason = "Integration cancellation" });
            _ = await service.CancelByCustomerAsync(
                first.Id,
                user.Id,
                new CancelOrderRequest { Reason = "Integration cancellation" });

            Assert.Equal(nameof(OrderStatus.Cancelled), cancelled.Status);
            Assert.NotNull(cancelled.CancelledAt);
            Assert.Null(cancelled.ExpiredAt);
            Assert.Equal("Integration cancellation", cancelled.CancellationReason);
            Assert.Equal(nameof(PaymentStatus.Cancelled), cancelled.Payment?.Status);
            Assert.Equal(
                [nameof(PaymentStatus.Pending), nameof(PaymentStatus.Cancelled)],
                cancelled.Payment!.StatusHistory.Select(history => history.ToStatus));
            Assert.Equal(
                [nameof(PaymentStatusChangeSource.Checkout), nameof(PaymentStatusChangeSource.OrderLifecycle)],
                cancelled.Payment.StatusHistory.Select(history => history.Source));
            Assert.Equal(5, await context.Products
                .Where(item => item.Id == product.Id)
                .Select(item => item.StockQuantity)
                .SingleAsync());
            Assert.Single(await context.InventoryTransactions
                .Where(item => item.OrderId == first.Id
                    && item.Type == InventoryTransactionType.OrderCancelled)
                .ToListAsync());
            Assert.Equal(2, await context.OrderStatusHistories
                .CountAsync(history => history.OrderId == first.Id));

            context.ChangeTracker.Clear();
            context.CartItems.Add(new CartItem
            {
                Id = Guid.NewGuid(),
                CartId = cart.Id,
                ProductId = product.Id,
                Quantity = 1,
                UnitPrice = product.Price
            });
            await context.SaveChangesAsync();

            var deliveryOrder = await service.PlaceOrderAsync(
                user.Id,
                request,
                "integration-checkout-delivery");
            Assert.Equal(nameof(OrderStatus.Pending), deliveryOrder.Status);

            _ = await service.UpdateStatusAsync(
                deliveryOrder.Id,
                user.Id,
                new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed });
            var dispatchRequest = new DispatchShipmentRequest
            {
                Carrier = "SQL Carrier",
                TrackingNumber = "SQL-TRACKING-001"
            };
            _ = await service.DispatchShipmentAsync(
                deliveryOrder.Id,
                user.Id,
                dispatchRequest);
            _ = await service.UpdateStatusAsync(
                deliveryOrder.Id,
                user.Id,
                new UpdateOrderStatusRequest
                {
                    Status = OrderStatus.DeliveryFailed,
                    Note = "Không liên hệ được người nhận"
                });
            _ = await service.DispatchShipmentAsync(
                deliveryOrder.Id,
                user.Id,
                dispatchRequest);
            var delivered = await service.MarkShipmentDeliveredAsync(
                deliveryOrder.Id,
                user.Id,
                new MarkShipmentDeliveredRequest());

            Assert.Equal(nameof(OrderStatus.Delivered), delivered.Status);
            Assert.Equal(nameof(PaymentStatus.Paid), delivered.Payment?.Status);
            Assert.Equal(
                [nameof(PaymentStatus.Pending), nameof(PaymentStatus.Paid)],
                delivered.Payment!.StatusHistory.Select(history => history.ToStatus));
            Assert.Equal(
                [nameof(PaymentStatusChangeSource.Checkout), nameof(PaymentStatusChangeSource.OrderLifecycle)],
                delivered.Payment.StatusHistory.Select(history => history.Source));
            Assert.Equal(4, await context.Products
                .Where(item => item.Id == product.Id)
                .Select(item => item.StockQuantity)
                .SingleAsync());
            Assert.Single(await context.InventoryTransactions
                .Where(item => item.OrderId == deliveryOrder.Id
                    && item.Type == InventoryTransactionType.OrderPlaced)
                .ToListAsync());
            Assert.Empty(await context.InventoryTransactions
                .Where(item => item.OrderId == deliveryOrder.Id
                    && item.Type == InventoryTransactionType.OrderCancelled)
                .ToListAsync());

            var deliveryHistory = await context.OrderStatusHistories
                .AsNoTracking()
                .Where(history => history.OrderId == deliveryOrder.Id)
                .OrderBy(history => history.CreatedAt)
                .ToListAsync();
            Assert.Equal(
                [
                    OrderStatus.Pending,
                    OrderStatus.Confirmed,
                    OrderStatus.Shipping,
                    OrderStatus.DeliveryFailed,
                    OrderStatus.Shipping,
                    OrderStatus.Delivered
                ],
                deliveryHistory.Select(history => history.ToStatus));
            Assert.Equal(
                [
                    null,
                    OrderStatus.Pending,
                    OrderStatus.Confirmed,
                    OrderStatus.Shipping,
                    OrderStatus.DeliveryFailed,
                    OrderStatus.Shipping
                ],
                deliveryHistory.Select(history => history.FromStatus));
            Assert.Equal(4, await context.PaymentStatusHistories.CountAsync());

            var report = await new ReportService(
                new ReportReadRepository(context)).GetSalesSummaryAsync(new SalesSummaryQuery
                {
                    From = DateTime.UtcNow.AddDays(-1),
                    To = DateTime.UtcNow.AddDays(1),
                    LowStockThreshold = 10,
                    TopProductLimit = 10
                });
            Assert.Equal(2, report.TotalOrders);
            Assert.Equal(1, report.DeliveredOrders);
            Assert.Equal(1, report.CancelledOrders);
            Assert.Equal(30_125.50m, report.GrossPaidAmount);
            Assert.Equal(0m, report.RefundedAmount);
            Assert.Equal(30_125.50m, report.NetRevenue);
            Assert.Equal(0m, report.PendingPaymentAmount);
            Assert.Equal(1, report.LowStockProductCount);
            Assert.Equal(1, report.OrdersByStatus
                .Single(item => item.Status == nameof(OrderStatus.Delivered)).Count);
            Assert.Equal(1, report.PaymentsByStatus
                .Single(item => item.Status == nameof(PaymentStatus.Paid)).Count);
            var topProduct = Assert.Single(report.TopSellingProducts);
            Assert.Equal(product.Id, topProduct.ProductId);
            Assert.Equal(1, topProduct.QuantitySold);
            Assert.Equal(125.50m, topProduct.Revenue);

            var requestedReturn = await service.RequestReturnAsync(
                deliveryOrder.Id,
                user.Id,
                new CreateReturnRequest
                {
                    Reason = "Sản phẩm còn nguyên trạng"
                });
            var approvedReturn = await service.ReviewReturnAsync(
                deliveryOrder.Id,
                user.Id,
                new ReviewReturnRequest
                {
                    Decision = ReturnReviewDecision.Approve,
                    Note = "Đủ điều kiện hoàn hàng"
                });
            var returned = await service.ReceiveReturnAsync(
                deliveryOrder.Id,
                user.Id,
                new ReceiveReturnRequest
                {
                    InspectionNote = "Đã nhận đủ sản phẩm và phụ kiện"
                });
            var refunded = await service.RecordRefundAsync(
                deliveryOrder.Id,
                user.Id,
                new RecordOrderRefundRequest
                {
                    Reference = "SQL-REFUND-001",
                    Note = "Đã hoàn tiền qua chuyển khoản"
                });
            _ = await service.RecordRefundAsync(
                deliveryOrder.Id,
                user.Id,
                new RecordOrderRefundRequest { Reference = "SQL-REFUND-001" });

            Assert.Equal(
                nameof(OrderStatus.ReturnRequested),
                requestedReturn.Status);
            Assert.Equal(
                nameof(OrderStatus.ReturnApproved),
                approvedReturn.Status);
            Assert.Equal(nameof(OrderStatus.Returned), returned.Status);
            Assert.Equal(nameof(OrderStatus.Refunded), refunded.Status);
            Assert.Equal(nameof(PaymentStatus.Refunded), refunded.Payment?.Status);
            Assert.Equal(5, await context.Products
                .Where(item => item.Id == product.Id)
                .Select(item => item.StockQuantity)
                .SingleAsync());
            Assert.Single(await context.InventoryTransactions
                .Where(item => item.OrderId == deliveryOrder.Id
                    && item.Type == InventoryTransactionType.OrderReturned)
                .ToListAsync());
            var refundHistory = Assert.Single(await context.PaymentStatusHistories
                .Where(history => history.PaymentId == refunded.Payment!.Id
                    && history.ToStatus == PaymentStatus.Refunded)
                .ToListAsync());
            Assert.Equal(PaymentStatusChangeSource.ManualRefund, refundHistory.Source);
            Assert.Equal("SQL-REFUND-001", refundHistory.Reference);
            Assert.Equal(10, await context.OrderStatusHistories
                .CountAsync(history => history.OrderId == deliveryOrder.Id));

            Assert.Equal(12, await context.OutboxMessages.CountAsync());

            var notificationSender = new RecordingNotificationSender();
            var outboxProcessor = new OutboxProcessor(
                new EfOutboxStore(context),
                new NotificationOutboxMessageHandler(
                    new UserRepository(context),
                    notificationSender,
                    NullLogger<NotificationOutboxMessageHandler>.Instance),
                Options.Create(new OutboxOptions
                {
                    BatchSize = 20,
                    MaxAttempts = 3,
                    LockTimeoutMinutes = 5,
                    PollIntervalSeconds = 1
                }),
                NullLogger<OutboxProcessor>.Instance);

            Assert.Equal(12, await outboxProcessor.ProcessBatchAsync());
            Assert.Equal(12, notificationSender.Messages.Count);
            Assert.All(
                await context.OutboxMessages.AsNoTracking().ToListAsync(),
                message => Assert.NotNull(message.ProcessedAt));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentReturnReceipts_RestoreInventoryExactlyOnce()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            Guid orderId;
            Guid productId;
            Guid actorUserId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    UserName = "return_concurrency_customer",
                    NormalizedUserName = "RETURN_CONCURRENCY_CUSTOMER",
                    Email = "return_concurrency@example.com",
                    NormalizedEmail = "RETURN_CONCURRENCY@EXAMPLE.COM",
                    FullName = "Return Concurrency Customer",
                    PasswordHash = "hash",
                    CreatedAt = DateTime.UtcNow
                };
                actorUserId = user.Id;
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Return concurrency",
                    NormalizedName = "RETURN CONCURRENCY"
                };
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = "Return concurrency product",
                    Price = 500_000m,
                    StockQuantity = 1,
                    CreatedAt = DateTime.UtcNow
                };
                productId = product.Id;
                var cart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id
                };
                setupContext.AddRange(
                    user,
                    category,
                    product,
                    cart,
                    new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = cart.Id,
                        ProductId = product.Id,
                        Quantity = 1,
                        UnitPrice = product.Price
                    });
                await setupContext.SaveChangesAsync();

                var service = CreateOrderService(setupContext);
                var order = await service.PlaceOrderAsync(
                    user.Id,
                    new PlaceOrderRequest
                    {
                        ShippingAddress = "1 Return Concurrency Street",
                        PaymentMethod = PaymentMethod.CashOnDelivery
                    },
                    "return-concurrency-checkout");
                orderId = order.Id;
                _ = await service.UpdateStatusAsync(
                    orderId,
                    actorUserId,
                    new UpdateOrderStatusRequest
                    {
                        Status = OrderStatus.Confirmed
                    });
                _ = await service.DispatchShipmentAsync(
                    orderId,
                    actorUserId,
                    new DispatchShipmentRequest
                    {
                        Carrier = "Concurrency Carrier",
                        TrackingNumber =
                            $"RETURN-{Guid.NewGuid():N}"
                    });
                _ = await service.MarkShipmentDeliveredAsync(
                    orderId,
                    actorUserId,
                    new MarkShipmentDeliveredRequest());
                _ = await service.RequestReturnAsync(
                    orderId,
                    user.Id,
                    new CreateReturnRequest
                    {
                        Reason = "Sản phẩm còn nguyên trạng"
                    });
                _ = await service.ReviewReturnAsync(
                    orderId,
                    actorUserId,
                    new ReviewReturnRequest
                    {
                        Decision = ReturnReviewDecision.Approve
                    });
            }

            async Task<Exception?> ReceiveAsync()
            {
                await using var context = new AppDbContext(options);
                var service = CreateOrderService(context);
                return await Record.ExceptionAsync(() =>
                    service.ReceiveReturnAsync(
                        orderId,
                        actorUserId,
                        new ReceiveReturnRequest
                        {
                            InspectionNote =
                                "Đã nhận đủ sản phẩm và phụ kiện"
                        }));
            }

            var outcomes = await Task.WhenAll(
                ReceiveAsync(),
                ReceiveAsync());

            Assert.All(outcomes, Assert.Null);
            await using var verificationContext =
                new AppDbContext(options);
            Assert.Equal(
                1,
                await verificationContext.Products
                    .Where(product => product.Id == productId)
                    .Select(product => product.StockQuantity)
                    .SingleAsync());
            Assert.Equal(
                1,
                await verificationContext.InventoryTransactions
                    .CountAsync(transaction =>
                        transaction.OrderId == orderId
                        && transaction.Type
                            == InventoryTransactionType.OrderReturned));
            Assert.Equal(
                OrderStatus.Returned,
                await verificationContext.Orders
                    .Where(order => order.Id == orderId)
                    .Select(order => order.Status)
                    .SingleAsync());
            Assert.Equal(
                ReturnRequestStatus.Received,
                await verificationContext.ReturnRequests
                    .Where(returnRequest =>
                        returnRequest.OrderId == orderId)
                    .Select(returnRequest => returnRequest.Status)
                    .SingleAsync());
        }
        finally
        {
            await using var cleanupContext =
                new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentOutboxWorkers_DeliverEachMessageOnce()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendOutbox_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using (var seedContext = new AppDbContext(options))
            {
                await seedContext.Database.MigrateAsync();
                var user = new User
                {
                    Id = Guid.NewGuid(),
                    UserName = "outbox_integration",
                    NormalizedUserName = "OUTBOX_INTEGRATION",
                    Email = "outbox_integration@example.com",
                    NormalizedEmail = "OUTBOX_INTEGRATION@EXAMPLE.COM",
                    FullName = "Outbox Integration",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
                    CreatedAt = DateTime.UtcNow
                };
                seedContext.Users.Add(user);
                var writer = new OutboxWriter(
                    new OutboxRepository(seedContext));
                for (var index = 0; index < 20; index++)
                    writer.EnqueueNotification(user.Id, $"Subject {index}", $"Message {index}");

                await seedContext.SaveChangesAsync();
            }

            var sender = new ConcurrentRecordingNotificationSender();

            async Task<int> RunWorkerAsync()
            {
                await using var workerContext = new AppDbContext(options);
                var processor = new OutboxProcessor(
                    new EfOutboxStore(workerContext),
                    new NotificationOutboxMessageHandler(
                        new UserRepository(workerContext),
                        sender,
                        NullLogger<NotificationOutboxMessageHandler>.Instance),
                    Options.Create(new OutboxOptions
                    {
                        BatchSize = 20,
                        MaxAttempts = 3,
                        LockTimeoutMinutes = 5,
                        ProcessingTimeoutSeconds = 30,
                        PollIntervalSeconds = 1
                    }),
                    NullLogger<OutboxProcessor>.Instance);
                return await processor.ProcessBatchAsync();
            }

            var handled = await Task.WhenAll(RunWorkerAsync(), RunWorkerAsync());

            Assert.Equal(20, handled.Sum());
            Assert.Equal(20, sender.Deliveries.Count);
            Assert.All(sender.Deliveries.Values, count => Assert.Equal(1, count));

            await using var verificationContext = new AppDbContext(options);
            Assert.Equal(
                20,
                await verificationContext.OutboxMessages.CountAsync(
                    message => message.ProcessedAt != null
                        && message.DeadLetteredAt == null
                        && message.LockId == null));

            var releasableMessage = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = OutboxMessageTypes.NotificationRequested,
                Payload = "{}",
                OccurredAt = DateTime.UtcNow,
                NextAttemptAt = DateTime.UtcNow
            };
            verificationContext.OutboxMessages.Add(releasableMessage);
            await verificationContext.SaveChangesAsync();
            var store = new EfOutboxStore(verificationContext);
            var ownerLock = Guid.NewGuid();
            var claimed = await store.ClaimBatchAsync(
                ownerLock,
                1,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMinutes(-5));

            Assert.Equal(releasableMessage.Id, Assert.Single(claimed).Id);
            Assert.False(await store.ReleaseClaimAsync(releasableMessage.Id, Guid.NewGuid()));
            Assert.True(await store.ReleaseClaimAsync(releasableMessage.Id, ownerLock));
            verificationContext.ChangeTracker.Clear();
            var released = await verificationContext.OutboxMessages.AsNoTracking()
                .SingleAsync(message => message.Id == releasableMessage.Id);
            Assert.Null(released.LockId);
            Assert.Null(released.LockedAt);
            Assert.Equal(0, released.Attempts);
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }
    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task PaymentAuditMigration_BackfillsLegacyPaymentAndWebhookData()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendMigration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20260719110525_HardenOrderLifecycleAndInventory");

            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var createdAt = DateTime.UtcNow;
            var paidAt = new DateTime(2026, 7, 18, 9, 30, 0, DateTimeKind.Utc);
            var paymentCreatedAt = paidAt.AddMinutes(-5);
            var orderNumber = $"ORD-{Guid.NewGuid():N}"[..32];
            var idempotencyKey = Guid.NewGuid().ToString("N");
            var passwordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123");

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Users]
                    ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail],
                     [PasswordHash], [FullName], [Phone], [IsDeleted], [CreatedAt],
                     [PasswordChangedAt], [TokenVersion])
                VALUES
                    ({userId}, {"legacy_payment_customer"}, {"LEGACY_PAYMENT_CUSTOMER"},
                     {"legacy_payment_customer@example.com"}, {"LEGACY_PAYMENT_CUSTOMER@EXAMPLE.COM"},
                     {passwordHash}, {"Legacy Payment Customer"}, NULL, {false}, {createdAt}, NULL, {0})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Orders]
                    ([Id], [UserId], [OrderNumber], [IdempotencyKey], [IdempotencyRequestHash],
                     [OrderDate], [SubtotalAmount], [DiscountAmount], [ShippingFee], [TaxAmount],
                     [TotalAmount], [Status], [ShippingAddress], [Note])
                VALUES
                    ({orderId}, {userId}, {orderNumber}, {idempotencyKey}, {new string('B', 64)},
                     {createdAt}, {100m}, {0m}, {0m}, {0m}, {100m}, {(int)OrderStatus.Confirmed},
                     {"Legacy address"}, NULL)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Payments]
                    ([Id], [OrderId], [Method], [Status], [Amount], [Provider],
                     [ProviderTransactionId], [CreatedAt], [PaidAt])
                VALUES
                    ({paymentId}, {orderId}, {(int)PaymentMethod.CashOnDelivery},
                     {(int)PaymentStatus.Paid}, {100m}, {"generic-hmac"}, {"legacy-txn"},
                     {paymentCreatedAt}, {paidAt})
                """);

            const string payload = "{\"providerTransactionId\":\"legacy-txn\",\"status\":\"paid\"}";
            var eventId = Guid.NewGuid();
            var processedAt = paidAt.AddSeconds(10);
            var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [PaymentWebhookEvents]
                    ([Id], [PaymentId], [Provider], [ProviderEventId], [PayloadHash], [Payload], [ReceivedAt], [ProcessedAt])
                VALUES
                    ({eventId}, {paymentId}, {"generic-hmac"}, {"legacy-event"}, {payloadHash}, {payload}, {processedAt}, {processedAt})
                """);

            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            var migratedEvent = await context.PaymentWebhookEvents.AsNoTracking().SingleAsync();
            Assert.Equal(PaymentStatus.Paid, migratedEvent.ResultingStatus);
            Assert.True(migratedEvent.StatusChanged);
            Assert.Equal(processedAt, migratedEvent.OccurredAt);

            var migratedHistory = await context.PaymentStatusHistories.AsNoTracking().SingleAsync();
            Assert.Null(migratedHistory.FromStatus);
            Assert.Equal(PaymentStatus.Paid, migratedHistory.ToStatus);
            Assert.Equal(PaymentStatusChangeSource.LegacyBackfill, migratedHistory.Source);
            Assert.Equal(paidAt, migratedHistory.OccurredAt);
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task RefreshWaitsForUserSessionMutation_AndCannotSurviveRevocation()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendSession_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);

        try
        {
            AuthResponse registered;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var authService = TestServiceFactory.CreateAuthService(setupContext, clock);
                registered = await authService.RegisterAsync(new RegisterRequest
                {
                    UserName = "session_lock_customer",
                    Email = "session_lock_customer@example.com",
                    Password = "Customer@123",
                    FullName = "Session Lock Customer"
                });
            }

            await using var blockerContext = new AppDbContext(options);
            var consistency = new EfDataConsistencyService(blockerContext);
            await using var transaction = await consistency.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var user = await consistency.LockUserAsync(registered.UserId, activeOnly: true)
                ?? throw new InvalidOperationException("SQL session test user was not found.");
            var tokens = await blockerContext.RefreshTokens
                .Where(token => token.UserId == user.Id && token.RevokedAt == null)
                .ToListAsync();

            await using var refreshContext = new AppDbContext(options);
            var refreshService = TestServiceFactory.CreateAuthService(refreshContext, clock);
            var refreshTask = refreshService.RefreshAsync(new RefreshTokenRequest
            {
                RefreshToken = registered.RefreshToken
            });

            await Task.Delay(250);
            Assert.False(refreshTask.IsCompleted);

            foreach (var token in tokens)
                token.Revoke(now.UtcDateTime, "Logout all");
            user.InvalidateSessions();
            await blockerContext.SaveChangesAsync();
            await transaction.CommitAsync();

            var exception = await Assert.ThrowsAsync<ApiException>(async () =>
            {
                _ = await refreshTask;
            });
            Assert.Equal(401, exception.StatusCode);

            await using var verificationContext = new AppDbContext(options);
            var persistedUser = await verificationContext.Users
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == registered.UserId);
            var persistedTokens = await verificationContext.RefreshTokens
                .AsNoTracking()
                .Where(token => token.UserId == registered.UserId)
                .ToListAsync();
            Assert.Equal(1, persistedUser.TokenVersion);
            Assert.DoesNotContain(
                persistedTokens,
                token => token.IsActiveAt(now.UtcDateTime));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task FulfillmentMigration_BackfillsLegacyReturnedAndRefundedOrder()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendFulfillmentMigration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            await using var context = new AppDbContext(options);
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(
                "20260727180607_AddPricingAndPromotions");

            var userId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var paymentId = Guid.NewGuid();
            var orderDate = DateTime.UtcNow.AddDays(-5);
            var deliveredAt = orderDate.AddDays(2);
            var returnedAt = deliveredAt.AddDays(1);
            var refundedAt = returnedAt.AddHours(1);
            var orderNumber = $"ORD-{Guid.NewGuid():N}"[..32];
            var passwordHash =
                BCrypt.Net.BCrypt.HashPassword("Customer@123");

            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Users]
                    ([Id], [UserName], [NormalizedUserName], [Email],
                     [NormalizedEmail], [PasswordHash], [FullName],
                     [Phone], [IsDeleted], [CreatedAt],
                     [PasswordChangedAt], [TokenVersion])
                VALUES
                    ({userId}, {"legacy_return_customer"},
                     {"LEGACY_RETURN_CUSTOMER"},
                     {"legacy_return_customer@example.com"},
                     {"LEGACY_RETURN_CUSTOMER@EXAMPLE.COM"},
                     {passwordHash}, {"Legacy Return Customer"},
                     NULL, {false}, {orderDate}, NULL, {0})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Orders]
                    ([Id], [UserId], [OrderNumber], [IdempotencyKey],
                     [IdempotencyRequestHash], [OrderDate],
                     [SubtotalAmount], [DiscountAmount], [ShippingFee],
                     [TaxAmount], [TotalAmount], [Status],
                     [ShippingAddress], [Note])
                VALUES
                    ({orderId}, {userId}, {orderNumber},
                     {Guid.NewGuid().ToString("N")}, {new string('L', 64)},
                     {orderDate}, {100m}, {0m}, {0m}, {0m}, {100m},
                     {(int)OrderStatus.Returned}, {"Legacy address"}, NULL)
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [Payments]
                    ([Id], [OrderId], [Method], [Status], [Amount],
                     [Provider], [ProviderTransactionId], [CreatedAt],
                     [PaidAt])
                VALUES
                    ({paymentId}, {orderId},
                     {(int)PaymentMethod.CashOnDelivery},
                     {(int)PaymentStatus.Refunded}, {100m}, {"cod"},
                     {orderNumber}, {orderDate}, {deliveredAt})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [OrderStatusHistories]
                    ([Id], [OrderId], [ChangedByUserId], [FromStatus],
                     [ToStatus], [Note], [CreatedAt])
                VALUES
                    ({Guid.NewGuid()}, {orderId}, NULL,
                     {(int)OrderStatus.Shipping},
                     {(int)OrderStatus.Delivered}, NULL, {deliveredAt}),
                    ({Guid.NewGuid()}, {orderId}, NULL,
                     {(int)OrderStatus.Delivered},
                     {(int)OrderStatus.Returned}, NULL, {returnedAt})
                """);
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO [PaymentStatusHistories]
                    ([Id], [PaymentId], [ChangedByUserId], [FromStatus],
                     [ToStatus], [Source], [Reference], [OccurredAt],
                     [CreatedAt])
                VALUES
                    ({Guid.NewGuid()}, {paymentId}, NULL,
                     {(int)PaymentStatus.Paid},
                     {(int)PaymentStatus.Refunded},
                     {(int)PaymentStatusChangeSource.ManualRefund},
                     {"LEGACY-REFUND"}, {refundedAt}, {refundedAt})
                """);

            await migrator.MigrateAsync();
            context.ChangeTracker.Clear();

            var order = await context.Orders.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == orderId);
            var shipment = await context.Shipments.AsNoTracking()
                .SingleAsync(candidate => candidate.OrderId == orderId);
            var returnRequest = await context.ReturnRequests.AsNoTracking()
                .SingleAsync(candidate => candidate.OrderId == orderId);

            Assert.Equal(OrderStatus.Refunded, order.Status);
            Assert.Equal("Legacy", shipment.Carrier);
            Assert.Equal(deliveredAt, shipment.DeliveredAt);
            Assert.Equal(ReturnRequestStatus.Refunded, returnRequest.Status);
            Assert.Equal(refundedAt, returnRequest.RefundedAt);
            Assert.Equal(1, await context.OrderStatusHistories.CountAsync(
                history => history.OrderId == orderId
                    && history.ToStatus == OrderStatus.Refunded));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentDeadLetterRedrive_ResetsMessageAndAuditsExactlyOnce()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendRedrive_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var messageId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 21, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await using (var seedContext = new AppDbContext(options))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.OutboxMessages.Add(new OutboxMessage
                {
                    Id = messageId,
                    Type = OutboxMessageTypes.NotificationRequested,
                    Payload = "{}",
                    OccurredAt = now.UtcDateTime.AddMinutes(-10),
                    NextAttemptAt = now.UtcDateTime.AddMinutes(-1),
                    Attempts = 5,
                    LastAttemptAt = now.UtcDateTime.AddMinutes(-1),
                    DeadLetteredAt = now.UtcDateTime.AddMinutes(-1),
                    LastError = "test failure"
                });
                await seedContext.SaveChangesAsync();
            }

            async Task<RedriveOutboxResponse> RedriveAsync()
            {
                await using var context = new AppDbContext(options);
                var clock = new FixedTimeProvider(now);
                var audit = new AuditWriter(
                    new AuditRepository(context),
                    new HttpContextAccessor(),
                    clock);
                var consistency = new EfDataConsistencyService(context);
                var service = new OperationsService(
                    new DeadLetterUseCase(
                        new OutboxRepository(context),
                        context,
                        consistency,
                        audit,
                        clock),
                    new AuditQueryUseCase(new AuditRepository(context)),
                    new DataRetentionUseCase(
                        new DataRetentionRepository(context),
                        context,
                        consistency,
                        audit,
                        clock,
                        Options.Create(new DataRetentionOptions()),
                        NullLogger<DataRetentionUseCase>.Instance));
                return await service.RedriveDeadLetterAsync(messageId, actorUserId);
            }

            var outcomes = await Task.WhenAll(RedriveAsync(), RedriveAsync());

            Assert.Single(outcomes, outcome => outcome.ReDriven);
            Assert.Single(outcomes, outcome => !outcome.ReDriven);
            await using var verificationContext = new AppDbContext(options);
            var message = await verificationContext.OutboxMessages.AsNoTracking()
                .SingleAsync(item => item.Id == messageId);
            Assert.Equal(0, message.Attempts);
            Assert.Null(message.DeadLetteredAt);
            Assert.Null(message.LastError);
            Assert.Equal(1, await verificationContext.AuditEvents.CountAsync(item =>
                item.Action == "outbox.dead_letter.redrive"
                && item.EntityId == messageId.ToString()));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentDataRetentionRuns_DeleteEligibleOutboxExactlyOnce()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendRetention_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var messageId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

        try
        {
            await using (var seedContext = new AppDbContext(options))
            {
                await seedContext.Database.MigrateAsync();
                seedContext.OutboxMessages.Add(new OutboxMessage
                {
                    Id = messageId,
                    Type = OutboxMessageTypes.NotificationRequested,
                    Payload = "{}",
                    OccurredAt = now.UtcDateTime.AddDays(-31),
                    NextAttemptAt = now.UtcDateTime.AddDays(-31),
                    ProcessedAt = now.UtcDateTime.AddDays(-31)
                });
                await seedContext.SaveChangesAsync();
            }

            async Task<DataRetentionResponse> RunAsync()
            {
                await using var context = new AppDbContext(options);
                var clock = new FixedTimeProvider(now);
                var consistency = new EfDataConsistencyService(context);
                var audit = new AuditWriter(
                    new AuditRepository(context),
                    new HttpContextAccessor(),
                    clock);
                var service = new OperationsService(
                    new DeadLetterUseCase(
                        new OutboxRepository(context),
                        context,
                        consistency,
                        audit,
                        clock),
                    new AuditQueryUseCase(new AuditRepository(context)),
                    new DataRetentionUseCase(
                        new DataRetentionRepository(context),
                        context,
                        consistency,
                        audit,
                        clock,
                        Options.Create(new DataRetentionOptions
                        {
                            Enabled = true,
                            ProcessedOutboxRetentionDays = 30
                        }),
                        NullLogger<DataRetentionUseCase>.Instance));
                return await service.RunDataRetentionAsync(
                    new DataRetentionRequest { ApplyChanges = true, MaxBatchSize = 10 },
                    actorUserId);
            }

            var outcomes = await Task.WhenAll(RunAsync(), RunAsync());

            Assert.Equal(1, outcomes.Sum(outcome => outcome.ProcessedOutboxDeletedCount));
            await using var verificationContext = new AppDbContext(options);
            Assert.False(await verificationContext.OutboxMessages.AnyAsync(item => item.Id == messageId));
            Assert.Equal(1, await verificationContext.AuditEvents.CountAsync(item =>
                item.Action == "operations.data_retention.apply"));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentMainImageUploads_AreSerializedByProductLock()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendImages_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var root = Path.Combine(
            Path.GetTempPath(),
            "ECommerceBackend.SqlImageTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Guid productId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Concurrent Images",
                    NormalizedName = "CONCURRENT IMAGES"
                };
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = "Concurrent Image Product",
                    Description = "Concurrent image product",
                    Price = 20m,
                    StockQuantity = 1,
                    CreatedAt = DateTime.UtcNow
                };
                productId = product.Id;
                setupContext.AddRange(category, product);
                await setupContext.SaveChangesAsync();
            }

            var environment = new TestWebHostEnvironment(root);
            await using var firstContext = new AppDbContext(options);
            await using var secondContext = new AppDbContext(options);
            var firstService = TestServiceFactory.CreateUploadService(firstContext, environment);
            var secondService = TestServiceFactory.CreateUploadService(secondContext, environment);

            var results = await Task.WhenAll(
                firstService.UploadProductImageAsync(
                    productId,
                    UploadServiceTests.CreatePng("first.png"),
                    isMain: true),
                secondService.UploadProductImageAsync(
                    productId,
                    UploadServiceTests.CreatePng("second.png"),
                    isMain: true));

            Assert.NotEqual(results[0].Id, results[1].Id);
            await using var verificationContext = new AppDbContext(options);
            var images = await verificationContext.ProductImages
                .AsNoTracking()
                .Where(image => image.ProductId == productId)
                .ToListAsync();
            Assert.Equal(2, images.Count);
            Assert.Single(images, image => image.IsMain);
            Assert.Equal(
                2,
                Directory.GetFiles(
                    Path.Combine(root, "Uploads", "products"),
                    "*",
                    SearchOption.TopDirectoryOnly).Length);
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentCategoryReparenting_UsesStableLocksAndPreventsCycle()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendCategories_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        try
        {
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                setupContext.Categories.AddRange(
                    new Category
                    {
                        Id = firstId,
                        Name = "Category A",
                        NormalizedName = "CATEGORY A"
                    },
                    new Category
                    {
                        Id = secondId,
                        Name = "Category B",
                        NormalizedName = "CATEGORY B"
                    });
                await setupContext.SaveChangesAsync();
            }

            await using var firstContext = new AppDbContext(options);
            await using var secondContext = new AppDbContext(options);
            var firstService = TestServiceFactory.CreateCategoryService(firstContext);
            var secondService = TestServiceFactory.CreateCategoryService(secondContext);

            static async Task<Exception?> CaptureAsync(Func<Task> action)
            {
                try
                {
                    await action();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            var outcomes = await Task.WhenAll(
                CaptureAsync(async () =>
                {
                    _ = await firstService.UpdateAsync(firstId, new UpdateCategoryRequest
                    {
                        Name = "Category A",
                        ParentId = secondId
                    });
                }),
                CaptureAsync(async () =>
                {
                    _ = await secondService.UpdateAsync(secondId, new UpdateCategoryRequest
                    {
                        Name = "Category B",
                        ParentId = firstId
                    });
                }));

            Assert.Single(outcomes, outcome => outcome == null);
            var rejected = Assert.Single(outcomes, outcome => outcome != null);
            Assert.IsType<BusinessException>(rejected);

            await using var verificationContext = new AppDbContext(options);
            var categories = await verificationContext.Categories
                .AsNoTracking()
                .Where(category => category.Id == firstId || category.Id == secondId)
                .ToListAsync();
            Assert.Single(categories, category => category.ParentId.HasValue);
            Assert.DoesNotContain(categories, category => category.ParentId == category.Id);
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentPaidWebhookAndCancellation_PreserveOrderPaymentInvariant()
    {
        SqlServerIntegrationTestGate.Require();

        const string webhookSecret = "test-payment-webhook-secret-32-bytes-minimum";
        var databaseName = $"ECommerceBackendPaymentRace_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var occurredAt = new DateTimeOffset(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(occurredAt);
        var orderId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        try
        {
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var user = new User
                {
                    Id = userId,
                    UserName = "payment_race_customer",
                    NormalizedUserName = "PAYMENT_RACE_CUSTOMER",
                    Email = "payment_race_customer@example.com",
                    NormalizedEmail = "PAYMENT_RACE_CUSTOMER@EXAMPLE.COM",
                    FullName = "Payment Race Customer",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
                    CreatedAt = occurredAt.UtcDateTime.AddMinutes(-5)
                };
                var order = new Order
                {
                    Id = orderId,
                    UserId = userId,
                    OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    IdempotencyRequestHash = new string('C', 64),
                    OrderDate = occurredAt.UtcDateTime.AddMinutes(-4),
                    ShippingAddress = "Payment race address"
                };
                order.SetPricing(100m, discount: 0, shipping: 0, tax: 0);
                var payment = new Payment
                {
                    Id = paymentId,
                    OrderId = orderId,
                    Method = PaymentMethod.CashOnDelivery,
                    Amount = 100m,
                    Provider = "generic-hmac",
                    ProviderTransactionId = "txn-payment-race",
                    CreatedAt = occurredAt.UtcDateTime.AddMinutes(-3)
                };
                setupContext.AddRange(
                    user,
                    order,
                    payment,
                    new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        ChangedByUserId = userId,
                        FromStatus = null,
                        ToStatus = OrderStatus.Pending,
                        CreatedAt = order.OrderDate
                    },
                    new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = paymentId,
                        ChangedByUserId = userId,
                        FromStatus = null,
                        ToStatus = PaymentStatus.Pending,
                        Source = PaymentStatusChangeSource.Checkout,
                        Reference = order.OrderNumber,
                        OccurredAt = payment.CreatedAt,
                        CreatedAt = payment.CreatedAt
                    });
                await setupContext.SaveChangesAsync();
            }

            var webhookOptions = Options.Create(new PaymentWebhookOptions
            {
                Enabled = true,
                ProviderCode = "generic-hmac",
                Secret = webhookSecret,
                MaxPayloadBytes = 65_536,
                MaxFutureSkewMinutes = 5
            });
            const string payload = "{\"providerTransactionId\":\"txn-payment-race\",\"status\":\"paid\",\"amount\":100,\"occurredAt\":\"2026-07-19T12:00:00Z\"}";
            const string eventId = "evt-payment-race";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var signature = Convert.ToHexString(hmac.ComputeHash(
                Encoding.UTF8.GetBytes($"{eventId}.{payload}")));

            await using var orderContext = new AppDbContext(options);
            await using var webhookContext = new AppDbContext(options);
            var orderService = TestServiceFactory.CreateOrderService(
                orderContext,
                clock);
            var webhookService = new PaymentWebhookService(
                new PaymentRepository(webhookContext),
                webhookContext,
                new EfDataConsistencyService(webhookContext),
                new PaymentProviderResolver(
                [
                    new GenericHmacPaymentProvider(webhookOptions, clock)
                ]),
                new OutboxWriter(new OutboxRepository(webhookContext)),
                webhookOptions,
                clock);

            static async Task<Exception?> CaptureAsync(Func<Task> action)
            {
                try
                {
                    await action();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            var outcomes = await Task.WhenAll(
                CaptureAsync(async () =>
                {
                    _ = await orderService.UpdateStatusAsync(
                        orderId,
                        userId,
                        new UpdateOrderStatusRequest { Status = OrderStatus.Cancelled });
                }),
                CaptureAsync(async () =>
                {
                    _ = await webhookService.HandleAsync(
                        "generic-hmac",
                        new PaymentWebhookRequest(eventId, signature, payload));
                }));

            Assert.Single(outcomes, outcome => outcome == null);
            var rejected = Assert.Single(outcomes, outcome => outcome != null)!;
            Assert.True(rejected is BusinessException or ConflictException);

            await using var verificationContext = new AppDbContext(options);
            var persistedOrder = await verificationContext.Orders
                .AsNoTracking()
                .SingleAsync(order => order.Id == orderId);
            var persistedPayment = await verificationContext.Payments
                .AsNoTracking()
                .SingleAsync(payment => payment.Id == paymentId);
            Assert.False(
                persistedOrder.Status == OrderStatus.Cancelled
                    && persistedPayment.Status == PaymentStatus.Paid);
            Assert.True(
                persistedOrder.Status == OrderStatus.Pending
                    && persistedPayment.Status == PaymentStatus.Paid
                || persistedOrder.Status == OrderStatus.Cancelled
                    && persistedPayment.Status == PaymentStatus.Cancelled);
            Assert.Equal(1, await verificationContext.OutboxMessages.CountAsync());
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task StaffConfirmationAndExpirationRace_PreservesOrderAndInventoryState()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendExpirationRace_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTime(2026, 7, 21, 12, 0, 0, DateTimeKind.Utc);
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        try
        {
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Expiration race",
                    NormalizedName = $"EXPIRATION_RACE_{Guid.NewGuid():N}"
                };
                var user = new User
                {
                    Id = userId,
                    UserName = "expiration_race_customer",
                    NormalizedUserName = "EXPIRATION_RACE_CUSTOMER",
                    Email = "expiration_race@example.com",
                    NormalizedEmail = "EXPIRATION_RACE@EXAMPLE.COM",
                    FullName = "Expiration Race Customer",
                    PasswordHash = "hash",
                    CreatedAt = now.AddDays(-1)
                };
                var product = new Product
                {
                    Id = productId,
                    CategoryId = category.Id,
                    Name = "Last reserved product",
                    Price = 100,
                    StockQuantity = 0,
                    CreatedAt = now.AddDays(-1)
                };
                var order = new Order
                {
                    Id = orderId,
                    UserId = userId,
                    OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    IdempotencyRequestHash = new string('A', 64),
                    OrderDate = now.AddMinutes(-31),
                    ShippingAddress = "1 Race Street"
                };
                order.SetPricing(100, 0, 0, 0);
                order.SetPendingExpiration(now.AddMinutes(-1));
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    Method = PaymentMethod.CashOnDelivery,
                    Amount = 100,
                    Provider = "cod",
                    ProviderTransactionId = order.OrderNumber,
                    CreatedAt = order.OrderDate
                };

                setupContext.AddRange(
                    category,
                    user,
                    product,
                    order,
                    payment,
                    new OrderDetail
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        Quantity = 1,
                        UnitPrice = 100
                    },
                    new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = orderId,
                        ChangedByUserId = userId,
                        FromStatus = null,
                        ToStatus = OrderStatus.Pending,
                        CreatedAt = order.OrderDate
                    },
                    new PaymentStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        PaymentId = payment.Id,
                        ChangedByUserId = userId,
                        FromStatus = null,
                        ToStatus = PaymentStatus.Pending,
                        Source = PaymentStatusChangeSource.Checkout,
                        Reference = order.OrderNumber,
                        OccurredAt = order.OrderDate,
                        CreatedAt = order.OrderDate
                    },
                    new InventoryTransaction
                    {
                        Id = Guid.NewGuid(),
                        ProductId = productId,
                        OrderId = orderId,
                        CreatedByUserId = userId,
                        Type = InventoryTransactionType.OrderPlaced,
                        QuantityChange = -1,
                        BalanceAfter = 0,
                        Reason = $"Order {order.OrderNumber}",
                        CreatedAt = order.OrderDate
                    });
                await setupContext.SaveChangesAsync();
            }

            await using var staffContext = new AppDbContext(options);
            await using var expirationContext = new AppDbContext(options);
            var staffService = CreateOrderService(staffContext);
            var expirationService = CreateOrderService(expirationContext);

            static async Task<Exception?> CaptureAsync(Func<Task> action)
            {
                try
                {
                    await action();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            var outcomes = await Task.WhenAll(
                CaptureAsync(async () => _ = await staffService.UpdateStatusAsync(
                    orderId,
                    userId,
                    new UpdateOrderStatusRequest { Status = OrderStatus.Confirmed })),
                CaptureAsync(async () => _ = await expirationService.ExpirePendingOrderAsync(orderId, now)));

            Assert.Contains(outcomes, outcome => outcome == null);
            Assert.All(outcomes.Where(outcome => outcome != null), outcome =>
                Assert.True(outcome is BusinessException or ConflictException));

            await using var verificationContext = new AppDbContext(options);
            var orderState = await verificationContext.Orders.AsNoTracking()
                .SingleAsync(order => order.Id == orderId);
            var paymentState = await verificationContext.Payments.AsNoTracking()
                .SingleAsync(payment => payment.OrderId == orderId);
            var stock = await verificationContext.Products
                .Where(product => product.Id == productId)
                .Select(product => product.StockQuantity)
                .SingleAsync();
            var releaseCount = await verificationContext.InventoryTransactions.CountAsync(transaction =>
                transaction.OrderId == orderId
                && transaction.Type == InventoryTransactionType.OrderCancelled);

            if (orderState.Status == OrderStatus.Confirmed)
            {
                Assert.Equal(PaymentStatus.Pending, paymentState.Status);
                Assert.Equal(0, stock);
                Assert.Equal(0, releaseCount);
                Assert.Null(orderState.ExpiredAt);
            }
            else
            {
                Assert.Equal(OrderStatus.Cancelled, orderState.Status);
                Assert.Equal(PaymentStatus.Cancelled, paymentState.Status);
                Assert.Equal(1, stock);
                Assert.Equal(1, releaseCount);
                Assert.Equal("SystemExpired", orderState.CancellationReason);
                Assert.NotNull(orderState.ExpiredAt);
            }
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task SalesSummary_UsesHalfOpenUtcBoundariesAndHistoricalSnapshotsOnSqlServer()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendReporting_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var from = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddDays(1);

        try
        {
            await using var context = new AppDbContext(options);
            await context.Database.MigrateAsync();
            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = "report_boundary_customer",
                NormalizedUserName = "REPORT_BOUNDARY_CUSTOMER",
                Email = "report_boundary_customer@example.com",
                NormalizedEmail = "REPORT_BOUNDARY_CUSTOMER@EXAMPLE.COM",
                FullName = "Report Boundary Customer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Customer@123"),
                CreatedAt = from.AddDays(-10)
            };
            var category = new Category
            {
                Id = Guid.NewGuid(),
                Name = "Reporting Category",
                NormalizedName = "REPORTING CATEGORY"
            };
            var product = new Product
            {
                Id = Guid.NewGuid(),
                CategoryId = category.Id,
                Name = "Current Reporting Product",
                Description = "Reporting product",
                Price = 50m,
                StockQuantity = 4,
                CreatedAt = from.AddDays(-10)
            };

            Order CreateDeliveredOrder(DateTime orderDate, decimal amount)
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    OrderNumber = $"ORD-{Guid.NewGuid():N}"[..32],
                    IdempotencyKey = Guid.NewGuid().ToString("N"),
                    IdempotencyRequestHash = new string('R', 64),
                    OrderDate = orderDate,
                    ShippingAddress = "Reporting boundary address"
                };
                order.SetPricing(amount, discount: 0, shipping: 0, tax: 0);
                order.ChangeStatus(OrderStatus.Confirmed, null);
                order.ChangeStatus(OrderStatus.Shipping, null);
                order.ChangeStatus(OrderStatus.Delivered, null);
                return order;
            }

            Payment CreatePaidPayment(Order order, DateTime paidAt)
            {
                var payment = new Payment
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Method = PaymentMethod.CashOnDelivery,
                    Amount = order.TotalAmount,
                    Provider = "cod",
                    CreatedAt = order.OrderDate
                };
                payment.ChangeStatus(PaymentStatus.Paid, paidAt);
                return payment;
            }

            var includedOrder = CreateDeliveredOrder(from, 100m);
            var excludedAtToOrder = CreateDeliveredOrder(to, 200m);
            var refundedOrder = CreateDeliveredOrder(from.AddDays(-3), 50m);
            var includedPayment = CreatePaidPayment(includedOrder, from);
            var excludedAtToPayment = CreatePaidPayment(excludedAtToOrder, to);
            var refundedPayment = CreatePaidPayment(refundedOrder, from.AddDays(-2));
            refundedPayment.ChangeStatus(PaymentStatus.Refunded, from);

            context.AddRange(
                user,
                category,
                product,
                includedOrder,
                excludedAtToOrder,
                refundedOrder,
                includedPayment,
                excludedAtToPayment,
                refundedPayment,
                new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    OrderId = includedOrder.Id,
                    ProductId = product.Id,
                    ProductNameSnapshot = "Purchased Reporting Snapshot",
                    UnitPrice = 50m,
                    Quantity = 2
                },
                new OrderDetail
                {
                    Id = Guid.NewGuid(),
                    OrderId = excludedAtToOrder.Id,
                    ProductId = product.Id,
                    ProductNameSnapshot = "Excluded Boundary Snapshot",
                    UnitPrice = 50m,
                    Quantity = 4
                },
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = includedOrder.Id,
                    ToStatus = OrderStatus.Delivered,
                    CreatedAt = from
                },
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = excludedAtToOrder.Id,
                    ToStatus = OrderStatus.Delivered,
                    CreatedAt = to
                },
                new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = refundedOrder.Id,
                    ToStatus = OrderStatus.Delivered,
                    CreatedAt = refundedOrder.OrderDate
                },
                new PaymentStatusHistory
                {
                    Id = Guid.NewGuid(),
                    PaymentId = refundedPayment.Id,
                    FromStatus = PaymentStatus.Paid,
                    ToStatus = PaymentStatus.Refunded,
                    Source = PaymentStatusChangeSource.Webhook,
                    Reference = "evt-report-refund",
                    OccurredAt = from,
                    CreatedAt = from
                });
            await context.SaveChangesAsync();

            var report = await new ReportService(
                new ReportReadRepository(context)).GetSalesSummaryAsync(
                new SalesSummaryQuery
                {
                    From = from,
                    To = to,
                    LowStockThreshold = 5,
                    TopProductLimit = 10
                });

            Assert.Equal(from, report.From);
            Assert.Equal(to, report.To);
            Assert.Equal(1, report.TotalOrders);
            Assert.Equal(1, report.DeliveredOrders);
            Assert.Equal(100m, report.GrossPaidAmount);
            Assert.Equal(50m, report.RefundedAmount);
            Assert.Equal(50m, report.NetRevenue);
            Assert.Equal(1, report.LowStockProductCount);
            Assert.Equal(
                1,
                report.PaymentsByStatus.Single(
                    item => item.Status == nameof(PaymentStatus.Paid)).Count);
            var topProduct = Assert.Single(report.TopSellingProducts);
            Assert.Equal(product.Id, topProduct.ProductId);
            Assert.Equal("Purchased Reporting Snapshot", topProduct.ProductName);
            Assert.Equal(2, topProduct.QuantitySold);
            Assert.Equal(100m, topProduct.Revenue);
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task PasswordReset_ConcurrentUse_AllowsExactlyOneCommitAndRevokesSessions()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName = $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTimeOffset(2026, 7, 24, 13, 0, 0, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var protector = new TestSensitivePayloadProtector();
        Guid userId;
        string rawToken;

        try
        {
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var auth = TestServiceFactory.CreateAuthService(
                    setupContext,
                    clock,
                    payloadProtector: protector);
                var registered = await auth.RegisterAsync(new RegisterRequest
                {
                    UserName = "concurrent_reset_customer",
                    Email = "concurrent_reset_customer@example.com",
                    Password = "Customer@123",
                    FullName = "Concurrent Reset Customer"
                });
                userId = registered.UserId;

                await auth.RequestPasswordResetAsync(new ForgotPasswordRequest
                {
                    Email = "concurrent_reset_customer@example.com"
                });

                var firstMessage = await setupContext.OutboxMessages
                    .SingleAsync(candidate =>
                        candidate.Type
                            == OutboxMessageTypes.ProtectedNotificationRequested);
                await auth.RequestPasswordResetAsync(new ForgotPasswordRequest
                {
                    Email = "concurrent_reset_customer@example.com"
                });
                var message = await setupContext.OutboxMessages
                    .SingleAsync(candidate =>
                        candidate.Type
                            == OutboxMessageTypes.ProtectedNotificationRequested
                        && candidate.Id != firstMessage.Id);
                var payload = JsonSerializer.Deserialize<NotificationRequestedPayload>(
                    protector.Unprotect(message.Payload),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
                Assert.NotNull(payload);
                var resetUrl = new Uri(
                    payload.Message.Split('\n')[0].Split(": ", 2)[1]);
                rawToken = Uri.UnescapeDataString(
                    resetUrl.Query["?token=".Length..]);

                var issuedTokens = await setupContext.PasswordResetTokens
                    .AsNoTracking()
                    .OrderBy(token => token.Id)
                    .ToListAsync();
                Assert.Equal(2, issuedTokens.Count);
                Assert.Single(issuedTokens, token => token.RevokedAt.HasValue);
                Assert.Single(issuedTokens, token => token.IsActiveAt(now.UtcDateTime));
            }

            async Task<Exception?> TryResetAsync(string newPassword)
            {
                await using var context = new AppDbContext(options);
                var auth = TestServiceFactory.CreateAuthService(
                    context,
                    clock,
                    payloadProtector: protector);
                return await Record.ExceptionAsync(() =>
                    auth.ResetPasswordAsync(new ResetPasswordRequest
                    {
                        Token = rawToken,
                        NewPassword = newPassword
                    }));
            }

            var results = await Task.WhenAll(
                TryResetAsync("ChangedA@123"),
                TryResetAsync("ChangedB@123"));

            Assert.Single(results, result => result is null);
            var rejected = Assert.Single(results, result => result is not null);
            Assert.IsType<ApiException>(rejected);

            await using var verificationContext = new AppDbContext(options);
            var user = await verificationContext.Users
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == userId);
            var token = await verificationContext.PasswordResetTokens
                .AsNoTracking()
                .SingleAsync(candidate => candidate.ConsumedAt.HasValue);
            var refreshTokens = await verificationContext.RefreshTokens
                .AsNoTracking()
                .Where(candidate => candidate.UserId == userId)
                .ToListAsync();

            Assert.NotNull(token.ConsumedAt);
            Assert.Equal(1, user.TokenVersion);
            Assert.All(refreshTokens, candidate => Assert.True(candidate.IsRevoked));
            Assert.True(
                BCrypt.Net.BCrypt.Verify("ChangedA@123", user.PasswordHash)
                ^ BCrypt.Net.BCrypt.Verify("ChangedB@123", user.PasswordHash));
        }
        finally
        {
            await using var cleanupContext = new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentCheckoutAndStockAdjustment_PreserveInventoryLedger()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTimeOffset(
            2026,
            7,
            29,
            8,
            0,
            0,
            TimeSpan.Zero);
        const int initialStock = 5;
        const int adjustedStock = 10;

        try
        {
            Guid userId;
            Guid categoryId;
            Guid productId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Inventory concurrency",
                    NormalizedName = "INVENTORY CONCURRENCY"
                };
                var user = CreatePromotionCustomer(
                    "inventory_customer",
                    now);
                var cart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id
                };
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = "Inventory race product",
                    Price = 500_000m,
                    StockQuantity = initialStock,
                    Description = "Inventory race product",
                    CreatedAt = now.UtcDateTime
                };
                userId = user.Id;
                categoryId = category.Id;
                productId = product.Id;

                setupContext.AddRange(
                    category,
                    user,
                    cart,
                    product,
                    new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = cart.Id,
                        ProductId = product.Id,
                        Quantity = 1,
                        UnitPrice = product.Price
                    });
                await setupContext.SaveChangesAsync();
            }

            async Task<Exception?> CheckoutAsync()
            {
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(now));
                return await Record.ExceptionAsync(() =>
                    service.PlaceOrderAsync(
                        userId,
                        new PlaceOrderRequest
                        {
                            ShippingAddress =
                                "1 Inventory Concurrency Street",
                            PaymentMethod =
                                PaymentMethod.CashOnDelivery
                        },
                        "inventory-concurrency-checkout"));
            }

            async Task<Exception?> AdjustStockAsync()
            {
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateProductService(
                    context,
                    new FixedTimeProvider(now));
                return await Record.ExceptionAsync(() =>
                    service.UpdateAsync(
                        productId,
                        new UpdateProductRequest
                        {
                            CategoryId = categoryId,
                            Name = "Inventory race product",
                            Price = 500_000m,
                            StockQuantity = adjustedStock,
                            Description = "Inventory race product"
                        },
                        userId));
            }

            var outcomes = await Task.WhenAll(
                CheckoutAsync(),
                AdjustStockAsync());

            Assert.All(outcomes, Assert.Null);

            await using var verificationContext =
                new AppDbContext(options);
            var finalStock = await verificationContext.Products
                .Where(product => product.Id == productId)
                .Select(product => product.StockQuantity)
                .SingleAsync();
            var ledger = await verificationContext.InventoryTransactions
                .Where(transaction => transaction.ProductId == productId)
                .ToListAsync();

            Assert.Equal(2, ledger.Count);
            Assert.Single(
                ledger,
                transaction => transaction.Type
                    == InventoryTransactionType.OrderPlaced);
            Assert.Single(
                ledger,
                transaction => transaction.Type
                    == InventoryTransactionType.ManualAdjustment);
            Assert.Equal(
                finalStock,
                initialStock
                    + ledger.Sum(transaction =>
                        transaction.QuantityChange));
            Assert.Contains(
                finalStock,
                new[] { adjustedStock - 1, adjustedStock });
            Assert.Single(await verificationContext.Orders.ToListAsync());
            Assert.Empty(await verificationContext.CartItems.ToListAsync());
        }
        finally
        {
            await using var cleanupContext =
                new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentAdminDemotions_PreserveTheLastActiveAdmin()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTimeOffset(
            2026,
            7,
            29,
            8,
            0,
            0,
            TimeSpan.Zero);

        try
        {
            Guid actorUserId;
            Guid firstAdminId;
            Guid secondAdminId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var adminRole = await setupContext.Roles.SingleAsync(
                    role => role.Name == RoleNames.Admin);
                var staffRole = await setupContext.Roles.SingleAsync(
                    role => role.Name == RoleNames.Staff);
                var seededAdmin = await setupContext.Users.SingleAsync(
                    user => user.UserName == "admin");
                seededAdmin.IsDeleted = true;
                var actor = CreatePromotionCustomer(
                    "role_actor",
                    now);
                var firstAdmin = CreatePromotionCustomer(
                    "concurrent_admin_one",
                    now);
                var secondAdmin = CreatePromotionCustomer(
                    "concurrent_admin_two",
                    now);
                actorUserId = actor.Id;
                firstAdminId = firstAdmin.Id;
                secondAdminId = secondAdmin.Id;

                setupContext.AddRange(
                    actor,
                    firstAdmin,
                    secondAdmin,
                    new UserRole
                    {
                        UserId = actor.Id,
                        RoleId = staffRole.Id
                    },
                    new UserRole
                    {
                        UserId = firstAdmin.Id,
                        RoleId = adminRole.Id
                    },
                    new UserRole
                    {
                        UserId = secondAdmin.Id,
                        RoleId = adminRole.Id
                    });
                await setupContext.SaveChangesAsync();
            }

            async Task<(Guid UserId, Exception? Exception)> DemoteAsync(
                Guid userId)
            {
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateUserService(
                    context,
                    new FixedTimeProvider(now));
                var exception = await Record.ExceptionAsync(() =>
                    service.AssignRoleAsync(
                        actorUserId,
                        userId,
                        new AssignRoleRequest
                        {
                            RoleName = RoleNames.Staff
                        }));
                return (userId, exception);
            }

            var outcomes = await Task.WhenAll(
                DemoteAsync(firstAdminId),
                DemoteAsync(secondAdminId));

            Assert.Single(
                outcomes,
                outcome => outcome.Exception == null);
            var rejected = Assert.Single(
                outcomes,
                outcome => outcome.Exception != null);
            Assert.True(
                rejected.Exception is BusinessException
                {
                    Code: "last_admin_demotion_forbidden"
                }
                or ConflictException
                {
                    Code: "role_concurrency_conflict"
                });

            if (rejected.Exception is ConflictException)
            {
                await using var retryContext =
                    new AppDbContext(options);
                var retryService =
                    TestServiceFactory.CreateUserService(
                        retryContext,
                        new FixedTimeProvider(now));
                var retry = await Assert.ThrowsAsync<BusinessException>(
                    () => retryService.AssignRoleAsync(
                        actorUserId,
                        rejected.UserId,
                        new AssignRoleRequest
                        {
                            RoleName = RoleNames.Staff
                        }));
                Assert.Equal(
                    "last_admin_demotion_forbidden",
                    retry.Code);
            }

            await using var verificationContext =
                new AppDbContext(options);
            var activeAdminCount =
                await verificationContext.UserRoles.CountAsync(
                    userRole => userRole.Role != null
                        && userRole.Role.Name == RoleNames.Admin
                        && userRole.User != null
                        && !userRole.User.IsDeleted);
            var targetRoles = await verificationContext.UserRoles
                .Where(userRole =>
                    userRole.UserId == firstAdminId
                    || userRole.UserId == secondAdminId)
                .Include(userRole => userRole.Role)
                .ToListAsync();

            Assert.Equal(1, activeAdminCount);
            Assert.Equal(2, targetRoles.Count);
            Assert.Single(
                targetRoles,
                userRole => userRole.Role?.Name == RoleNames.Admin);
            Assert.Single(
                targetRoles,
                userRole => userRole.Role?.Name == RoleNames.Staff);
        }
        finally
        {
            await using var cleanupContext =
                new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task ConcurrentPromotionCheckouts_RespectGlobalUsageLimit()
    {
        SqlServerIntegrationTestGate.Require();

        var databaseName =
            $"ECommerceBackendIntegration_{Guid.NewGuid():N}";
        var connectionString = BuildConnectionString(databaseName);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        var now = new DateTimeOffset(
            2026,
            7,
            28,
            8,
            0,
            0,
            TimeSpan.Zero);

        try
        {
            Guid firstUserId;
            Guid secondUserId;
            await using (var setupContext = new AppDbContext(options))
            {
                await setupContext.Database.MigrateAsync();
                var category = new Category
                {
                    Id = Guid.NewGuid(),
                    Name = "Promotion concurrency",
                    NormalizedName = "PROMOTION CONCURRENCY"
                };
                var firstUser = CreatePromotionCustomer(
                    "promo_first",
                    now);
                var secondUser = CreatePromotionCustomer(
                    "promo_second",
                    now);
                firstUserId = firstUser.Id;
                secondUserId = secondUser.Id;
                var firstCart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = firstUser.Id
                };
                var secondCart = new Cart
                {
                    Id = Guid.NewGuid(),
                    UserId = secondUser.Id
                };
                var firstProduct = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = "Promotion product one",
                    Price = 600_000m,
                    StockQuantity = 1,
                    CreatedAt = now.UtcDateTime
                };
                var secondProduct = new Product
                {
                    Id = Guid.NewGuid(),
                    CategoryId = category.Id,
                    Name = "Promotion product two",
                    Price = 600_000m,
                    StockQuantity = 1,
                    CreatedAt = now.UtcDateTime
                };
                setupContext.AddRange(
                    category,
                    firstUser,
                    secondUser,
                    firstCart,
                    secondCart,
                    firstProduct,
                    secondProduct,
                    new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = firstCart.Id,
                        ProductId = firstProduct.Id,
                        Quantity = 1,
                        UnitPrice = firstProduct.Price
                    },
                    new CartItem
                    {
                        Id = Guid.NewGuid(),
                        CartId = secondCart.Id,
                        ProductId = secondProduct.Id,
                        Quantity = 1,
                        UnitPrice = secondProduct.Price
                    },
                    Promotion.Create(
                        Guid.NewGuid(),
                        "LASTONE",
                        PromotionType.FixedAmount,
                        100_000m,
                        500_000m,
                        maximumDiscountAmount: null,
                        now.UtcDateTime.AddDays(-1),
                        now.UtcDateTime.AddDays(1),
                        usageLimit: 1,
                        usageLimitPerCustomer: 1,
                        now.UtcDateTime));
                await setupContext.SaveChangesAsync();
            }

            async Task<Exception?> CheckoutAsync(
                Guid userId,
                string key)
            {
                await using var context = new AppDbContext(options);
                var service = TestServiceFactory.CreateOrderService(
                    context,
                    new FixedTimeProvider(now));
                return await Record.ExceptionAsync(() =>
                    service.PlaceOrderAsync(
                        userId,
                        new PlaceOrderRequest
                        {
                            ShippingAddress =
                                "1 Promotion Concurrency Street",
                            PaymentMethod =
                                PaymentMethod.CashOnDelivery,
                            PromotionCode = "LASTONE"
                        },
                        key));
            }

            var outcomes = await Task.WhenAll(
                CheckoutAsync(
                    firstUserId,
                    "promotion-first"),
                CheckoutAsync(
                    secondUserId,
                    "promotion-second"));

            Assert.Single(
                outcomes,
                outcome => outcome is null);
            var rejected = Assert.IsType<ConflictException>(
                Assert.Single(
                    outcomes,
                    outcome => outcome is not null));
            Assert.Equal(
                "promotion_usage_limit_reached",
                rejected.Code);

            await using var verificationContext =
                new AppDbContext(options);
            Assert.Equal(
                1,
                await verificationContext.Promotions
                    .Select(promotion => promotion.UsedCount)
                    .SingleAsync());
            Assert.Equal(
                1,
                await verificationContext.PromotionRedemptions
                    .CountAsync());
            Assert.Equal(
                1,
                await verificationContext.Orders.CountAsync());
            Assert.Equal(
                1,
                await verificationContext.CartItems.CountAsync());
        }
        finally
        {
            await using var cleanupContext =
                new AppDbContext(options);
            await cleanupContext.Database.EnsureDeletedAsync();
        }
    }

    private static OrderService CreateOrderService(AppDbContext context)
        => TestServiceFactory.CreateOrderService(context);

    private static User CreatePromotionCustomer(
        string userName,
        DateTimeOffset now)
        => new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail =
                $"{userName}@example.com".ToUpperInvariant(),
            FullName = userName,
            PasswordHash = "hash",
            CreatedAt = now.UtcDateTime
        };

    private static string BuildConnectionString(string databaseName)
        => SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(databaseName);

    private sealed class ConcurrentRecordingNotificationSender : INotificationSender
    {
        public ConcurrentDictionary<Guid, int> Deliveries { get; } = new();

        public async Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(5, cancellationToken);
            Deliveries.AddOrUpdate(idempotencyKey, 1, (_, count) => count + 1);
        }
    }
    private sealed class RecordingNotificationSender : INotificationSender
    {
        public List<Guid> Messages { get; } = [];

        public Task SendAsync(
            string recipientEmail,
            string subject,
            string message,
            Guid idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(idempotencyKey);
            return Task.CompletedTask;
        }
    }
}
