using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;

namespace ECommerceBackend.Tests;

public sealed class SqlServerIntegrationTestGateTests
{
    [Fact]
    public void CreateTestDatabaseConnectionString_UsesDedicatedIntegrationConnection()
    {
        var connectionString = SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(
            "Server=localhost;Database=ECommerceBackendIntegration;Trusted_Connection=True;TrustServerCertificate=True;",
            "ECommerceBackendReporting_123");

        var builder = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("ECommerceBackendReporting_123", builder.InitialCatalog);
    }

    [Fact]
    public void CreateTestDatabaseConnectionString_RejectsProductionLikeDatabase()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(
                "Server=localhost;Database=ECommerceDB;Trusted_Connection=True;TrustServerCertificate=True;",
                "ECommerceBackendReporting_123"));

        Assert.Contains("dedicated database", exception.Message);
    }

    [Fact]
    public void CreateTestDatabaseConnectionString_RejectsUnexpectedTestDatabaseName()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SqlServerIntegrationTestGate.CreateTestDatabaseConnectionString(
                "Server=localhost;Database=ECommerceBackendIntegration;Trusted_Connection=True;TrustServerCertificate=True;",
                "master"));

        Assert.Contains("may only create databases", exception.Message);
    }
}
