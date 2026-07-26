using Microsoft.Data.SqlClient;

namespace ECommerceBackend.Tests.Support;

internal static class SqlServerPerformanceTestGate
{
    private const string RunTestsVariable = "RUN_PERFORMANCE_TESTS";
    private const string ConnectionStringVariable = "ECOMMERCE_TEST_SQL_CONNECTION";
    private const string TestDatabasePrefix = "ECommerceBackendPerformance_";

    public static void Require()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RunTestsVariable),
                "1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Performance tests require {RunTestsVariable}=1. " +
                "Exclude Category=SqlServerPerformance from the regular test suite.");
        }

        _ = CreateDatabaseConnectionString($"{TestDatabasePrefix}Validation");
    }

    public static string CreateDatabaseConnectionString(string databaseName)
    {
        var baseConnectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionStringVariable} must be configured for performance tests.");

        return CreateDatabaseConnectionString(baseConnectionString, databaseName);
    }

    internal static string CreateDatabaseConnectionString(
        string baseConnectionString,
        string databaseName)
    {
        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(baseConnectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not a valid SQL Server connection string.",
                ex);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || string.IsNullOrWhiteSpace(builder.InitialCatalog)
            || (!builder.InitialCatalog.Contains("integration", StringComparison.OrdinalIgnoreCase)
                && !builder.InitialCatalog.Contains("performance", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} must target a dedicated database whose name contains " +
                "'integration' or 'performance'.");
        }

        if (!databaseName.StartsWith(TestDatabasePrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Performance tests may only create databases beginning with '{TestDatabasePrefix}'.");
        }

        builder.InitialCatalog = databaseName;
        return builder.ConnectionString;
    }
}
