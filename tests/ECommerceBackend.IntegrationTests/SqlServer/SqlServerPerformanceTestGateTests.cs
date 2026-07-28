using ECommerceBackend.Tests.Support;
using Microsoft.Data.SqlClient;

namespace ECommerceBackend.Tests;

public sealed class SqlServerPerformanceTestGateTests
{
    [Fact]
    public void CreateDatabaseConnectionString_UsesDedicatedPerformanceConnection()
    {
        var connectionString = SqlServerPerformanceTestGate.CreateDatabaseConnectionString(
            "Server=localhost;Database=ECommerceBackendPerformance;Trusted_Connection=True;",
            "ECommerceBackendPerformance_123");

        var builder = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("ECommerceBackendPerformance_123", builder.InitialCatalog);
    }

    [Fact]
    public void CreateDatabaseConnectionString_AcceptsDedicatedIntegrationConnection()
    {
        var connectionString = SqlServerPerformanceTestGate.CreateDatabaseConnectionString(
            "Server=localhost;Database=ECommerceBackendIntegration;Trusted_Connection=True;",
            "ECommerceBackendPerformance_123");

        var builder = new SqlConnectionStringBuilder(connectionString);
        Assert.Equal("ECommerceBackendPerformance_123", builder.InitialCatalog);
    }

    [Fact]
    public void CreateDatabaseConnectionString_RejectsProductionLikeDatabase()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SqlServerPerformanceTestGate.CreateDatabaseConnectionString(
                "Server=localhost;Database=ECommerceBackend;Trusted_Connection=True;",
                "ECommerceBackendPerformance_123"));

        Assert.Contains("dedicated database", exception.Message);
    }

    [Fact]
    public void CreateDatabaseConnectionString_RejectsUnexpectedDatabaseName()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SqlServerPerformanceTestGate.CreateDatabaseConnectionString(
                "Server=localhost;Database=ECommerceBackendPerformance;Trusted_Connection=True;",
                "master"));

        Assert.Contains("may only create databases", exception.Message);
    }
}
