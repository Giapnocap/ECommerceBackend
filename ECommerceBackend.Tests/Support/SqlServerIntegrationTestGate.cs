namespace ECommerceBackend.Tests.Support;

internal static class SqlServerIntegrationTestGate
{
    private const string RunTestsVariable = "RUN_SQL_INTEGRATION_TESTS";
    private const string ConnectionStringVariable = "ECOMMERCE_TEST_SQL_CONNECTION";

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
    }

    public static string GetConnectionString()
        => Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? throw new InvalidOperationException(
                $"{ConnectionStringVariable} must be configured for SQL Server integration tests.");
}
