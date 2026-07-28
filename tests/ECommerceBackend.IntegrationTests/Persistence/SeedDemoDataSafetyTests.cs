namespace ECommerceBackend.Tests;

public sealed class SeedDemoDataSafetyTests
{
    [Fact]
    public async Task DemoSeed_RequiresExplicitNonProductionEnvironment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "scripts", "SeedDemoData.sql"));
        var readme = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "README.md"));

        Assert.Contains("$(EnvironmentName)", script, StringComparison.Ordinal);
        Assert.DoesNotContain(
            ":setvar EnvironmentName",
            script,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "NOT IN (N'DEVELOPMENT', N'LOCAL', N'TESTING')",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "UPPER(DB_NAME()) LIKE N'%PROD%'",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "-v EnvironmentName=Development",
            readme,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null
            && !File.Exists(Path.Combine(directory.FullName, "ECommerceBackend.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
