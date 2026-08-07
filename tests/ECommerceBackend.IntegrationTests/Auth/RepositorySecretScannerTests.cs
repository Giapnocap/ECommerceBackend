using System.Diagnostics;

namespace ECommerceBackend.Tests;

public sealed class RepositorySecretScannerTests
{
    [Fact]
    public async Task Scanner_AllowsHttpVariablePlaceholders()
    {
        const string content = """
            @AdminPassword = replace-with-local-admin-password
            @AdminToken =
            @CustomerPassword = replace-with-local-customer-password

            {
              "password": "{{AdminPassword}}"
            }
            """;

        var result = await RunScannerAsync(content);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "No high-confidence secrets were found",
            result.Output,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("@AdminPassword = P@ssw0rd-DoNotCommit-2026!", "HttpCredential")]
    [InlineData("{\"password\":\"P@ssw0rd-DoNotCommit-2026!\"}", "JsonCredential")]
    [InlineData(
        "@AdminToken = eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.signatureValue123",
        "JwtToken")]
    public async Task Scanner_RejectsSecretsInHttpCollection(
        string content,
        string expectedFinding)
    {
        var result = await RunScannerAsync(content);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            $"[{expectedFinding}]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "client.http:1",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scanner_RejectsCommittedBCryptHash()
    {
        var bcryptHash = string.Concat("$2", "b$12$", new string('A', 53));
        var result = await RunScannerAsync(
            $"PasswordHash = \"{bcryptHash}\"");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("[BCryptHash]", result.Output, StringComparison.Ordinal);
        Assert.Contains("client.http:1", result.Output, StringComparison.Ordinal);
    }

    private static async Task<ProcessResult> RunScannerAsync(string httpContent)
    {
        var repositoryRoot = FindRepositoryRoot();
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            $"ECommerceBackend.SecretScanner.{Guid.NewGuid():N}");
        var scriptsDirectory = Path.Combine(temporaryRoot, "scripts");
        Directory.CreateDirectory(scriptsDirectory);

        try
        {
            var scannerPath = Path.Combine(
                scriptsDirectory,
                "TestRepositorySecrets.ps1");
            File.Copy(
                Path.Combine(
                    repositoryRoot,
                    "scripts",
                    "TestRepositorySecrets.ps1"),
                scannerPath);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryRoot, "client.http"),
                httpContent);

            await EnsureSucceededAsync(
                "git",
                ["init", "--quiet"],
                temporaryRoot);
            await EnsureSucceededAsync(
                "git",
                ["add", "--all"],
                temporaryRoot);

            var executable = OperatingSystem.IsWindows()
                ? "powershell.exe"
                : "pwsh";
            var arguments = new List<string>
            {
                "-NoLogo",
                "-NoProfile"
            };
            if (OperatingSystem.IsWindows())
            {
                arguments.Add("-ExecutionPolicy");
                arguments.Add("Bypass");
            }
            arguments.Add("-File");
            arguments.Add(scannerPath);

            return await RunProcessAsync(
                executable,
                arguments,
                temporaryRoot);
        }
        finally
        {
            DeleteTemporaryDirectory(temporaryRoot);
        }
    }

    private static void DeleteTemporaryDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        foreach (var file in Directory.EnumerateFiles(
            path,
            "*",
            SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static async Task EnsureSucceededAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var result = await RunProcessAsync(
            executable,
            arguments,
            workingDirectory);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{executable}' failed: {result.Output}");
        }
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Could not start process '{executable}'.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + (await standardError));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "ECommerceBackend.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
