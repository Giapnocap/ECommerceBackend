using Microsoft.Data.SqlClient;

namespace ECommerceBackend.Tests.Support;

internal static class SqlServerIntegrationTestGate
{
    private const string RunTestsVariable = "RUN_SQL_INTEGRATION_TESTS";
    private const string ConnectionStringVariable = "ECOMMERCE_TEST_SQL_CONNECTION";
    private const string RequiredCatalogMarker = "integration";
    private const string TestDatabasePrefix = "ECommerceBackend";

    public static void Require()
    {
        var enabled = string.Equals(
            Environment.GetEnvironmentVariable(RunTestsVariable),
            "1",
            StringComparison.Ordinal);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (!enabled)
        {
            throw new InvalidOperationException(
                $"SQL Server integration tests require {RunTestsVariable}=1. " +
                "Exclude Category=SqlServerIntegration when running the local unit-test suite.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} must be configured when {RunTestsVariable}=1.");
        }

        ValidateBaseConnectionString(connectionString);
    }

    public static string CreateTestDatabaseConnectionString(string databaseName)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionStringVariable} must be configured for SQL Server integration tests.");

        return CreateTestDatabaseConnectionString(connectionString, databaseName);
    }

    internal static string CreateTestDatabaseConnectionString(
        string connectionString,
        string databaseName)
    {
        ValidateBaseConnectionString(connectionString);

        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.StartsWith(TestDatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"SQL Server integration tests may only create databases beginning with '{TestDatabasePrefix}'.");
        }

        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    private static void ValidateBaseConnectionString(string connectionString)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not a valid SQL Server connection string.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || !builder.InitialCatalog.Contains(RequiredCatalogMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} must target a dedicated database whose name contains '{RequiredCatalogMarker}'.");
        }
    }
}
