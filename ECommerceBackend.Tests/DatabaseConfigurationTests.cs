using ECommerceBackend.API.Extensions;
using ECommerceBackend.Domain.Entities;
using ECommerceBackend.Infrastructure.Data;
using ECommerceBackend.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerceBackend.Tests;

public sealed class DatabaseConfigurationTests
{
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
