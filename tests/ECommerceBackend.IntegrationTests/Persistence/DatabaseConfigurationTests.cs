using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerceBackend.Tests;

public sealed class DatabaseConfigurationTests
{
    [Fact]
    public void DesignTimeFactory_CreatesSqlServerContextWithoutApiHost()
    {
        using var context = new AppDbContextDesignTimeFactory()
            .CreateDbContext([]);

        Assert.Equal(
            "Microsoft.EntityFrameworkCore.SqlServer",
            context.Database.ProviderName);
        Assert.False(string.IsNullOrWhiteSpace(
            context.Database.GetConnectionString()));
    }

    [Fact]
    public void DatabaseRegistration_UsesConfiguredCommandTimeout()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=ECommerceTests;Trusted_Connection=True;TrustServerCertificate=True;",
                ["Database:CommandTimeoutSeconds"] = "45"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddECommerceDatabase(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(45, context.Database.GetCommandTimeout());
    }

    [Fact]
    public void DataRetentionQueries_HaveLeadingColumnIndexes()
    {
        using var context = TestAppDbContext.Create();

        AssertIndex(context, typeof(OutboxMessage), "IX_OutboxMessages_ProcessedAt", nameof(OutboxMessage.ProcessedAt));
        AssertIndex(context, typeof(RefreshToken), "IX_RefreshTokens_ExpiresAt", nameof(RefreshToken.ExpiresAt));
        AssertIndex(context, typeof(PaymentWebhookEvent), "IX_PaymentWebhookEvents_ReceivedAt", nameof(PaymentWebhookEvent.ReceivedAt));
    }

    [Fact]
    public void PromotionUsage_HasCodeAndOrderUniqueness()
    {
        using var context = TestAppDbContext.Create();

        var promotionCode = context.Model
            .FindEntityType(typeof(Promotion))!
            .GetIndexes()
            .Single(index =>
                index.GetDatabaseName()
                    == "UX_Promotions_NormalizedCode");
        var redemptionOrder = context.Model
            .FindEntityType(typeof(PromotionRedemption))!
            .GetIndexes()
            .Single(index =>
                index.GetDatabaseName()
                    == "UX_PromotionRedemptions_OrderId");

        Assert.True(promotionCode.IsUnique);
        Assert.Equal(
            nameof(Promotion.NormalizedCode),
            Assert.Single(promotionCode.Properties).Name);
        Assert.True(redemptionOrder.IsUnique);
        Assert.Equal(
            nameof(PromotionRedemption.OrderId),
            Assert.Single(redemptionOrder.Properties).Name);
    }

    [Fact]
    public void PersistenceExceptionClassification_IsOwnedByEfAdapter()
    {
        using var context = TestAppDbContext.Create();
        var consistency = new EfDataConsistencyService(context);

        Assert.True(consistency.IsConcurrencyConflict(new DbUpdateConcurrencyException()));
        Assert.False(consistency.IsConcurrencyConflict(new InvalidOperationException()));
        Assert.False(consistency.IsUniqueConstraintViolation(new DbUpdateException()));
    }

    [Fact]
    public void Fulfillment_HasOneShipmentAndReturnRequestPerOrder()
    {
        using var context = TestAppDbContext.Create();

        var shipmentOrder = context.Model
            .FindEntityType(typeof(Shipment))!
            .GetIndexes()
            .Single(index =>
                index.GetDatabaseName() == "UX_Shipments_OrderId");
        var returnOrder = context.Model
            .FindEntityType(typeof(ReturnRequest))!
            .GetIndexes()
            .Single(index =>
                index.GetDatabaseName() == "UX_ReturnRequests_OrderId");

        Assert.True(shipmentOrder.IsUnique);
        Assert.True(returnOrder.IsUnique);
        Assert.Equal(
            nameof(Shipment.OrderId),
            Assert.Single(shipmentOrder.Properties).Name);
        Assert.Equal(
            nameof(ReturnRequest.OrderId),
            Assert.Single(returnOrder.Properties).Name);
    }

    [Fact]
    public void OrderRecipientSnapshot_HasBoundedNullabilityContract()
    {
        using var context = TestAppDbContext.Create();
        var order = context.Model.FindEntityType(typeof(Order))!;
        var recipientName = order.FindProperty(nameof(Order.RecipientName))!;
        var recipientPhone = order.FindProperty(nameof(Order.RecipientPhone))!;

        Assert.False(recipientName.IsNullable);
        Assert.Equal(100, recipientName.GetMaxLength());
        Assert.True(recipientPhone.IsNullable);
        Assert.Equal(20, recipientPhone.GetMaxLength());
    }

    private static void AssertIndex(
        AppDbContext context,
        Type entityType,
        string databaseName,
        string propertyName)
    {
        var index = context.Model.FindEntityType(entityType)!
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == databaseName);

        Assert.Equal(propertyName, Assert.Single(index.Properties).Name);
    }
}
