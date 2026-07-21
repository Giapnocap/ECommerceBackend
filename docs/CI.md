# CI Verification

The GitHub Actions workflow has two jobs:

- `build-and-unit-tests` restores with NuGet vulnerability auditing, verifies formatting, builds Release, validates that the EF model has no pending migration, runs tests outside the `SqlServerIntegration` category, enforces the coverage regression gate, and uploads the Cobertura report.
- `sql-server-integration-tests` starts SQL Server 2022, enables the integration-test gate, and runs only the SQL Server category.

Configure these two jobs as required checks in repository branch protection after a remote repository is connected.

The current coverage regression gate is 60% line coverage and 40% branch coverage. It protects the tested baseline; raise it only after adding meaningful tests, never by excluding production code.

SQL Server integration tests require both environment variables:

```text
RUN_SQL_INTEGRATION_TESTS=1
ECOMMERCE_TEST_SQL_CONNECTION=<dedicated SQL Server connection string>
```

When these variables are absent, selecting the integration-test category fails immediately. The local unit-test command excludes that category explicitly. This prevents a local or CI run from reporting false integration-test success.

Run the local suites separately:

```powershell
dotnet restore ECommerceBackend.sln
dotnet build ECommerceBackend.sln -c Release --no-restore
dotnet test ECommerceBackend.Tests\ECommerceBackend.Tests.csproj -c Release --no-build --settings coverage.runsettings --filter "Category!=SqlServerIntegration"

$env:RUN_SQL_INTEGRATION_TESTS = "1"
$env:ECOMMERCE_TEST_SQL_CONNECTION = "<dedicated SQL Server connection string>"
dotnet test ECommerceBackend.Tests\ECommerceBackend.Tests.csproj -c Release --no-build --filter "Category=SqlServerIntegration"
```

Each SQL integration test creates a database with a unique name and deletes it in a `finally` block. The test SQL identity must be restricted to the dedicated CI instance, while still being allowed to create and drop those temporary databases.

Protect the default branch by making both workflow jobs required checks. Do not use a production connection string for integration tests.
