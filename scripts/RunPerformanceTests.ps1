[CmdletBinding()]
param(
    [string]$ConnectionString = $env:ECOMMERCE_TEST_SQL_CONNECTION,
    [string]$ResultsDirectory = "./PerformanceResults",
    [string]$Configuration = "Release",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "Provide -ConnectionString or set ECOMMERCE_TEST_SQL_CONNECTION."
}

if ([System.IO.Path]::IsPathRooted($ResultsDirectory)) {
    $resolvedResultsDirectory = [System.IO.Path]::GetFullPath($ResultsDirectory)
}
else {
    $resolvedResultsDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path (Get-Location).Path $ResultsDirectory))
}
$env:RUN_PERFORMANCE_TESTS = "1"
$env:ECOMMERCE_TEST_SQL_CONNECTION = $ConnectionString
$env:ECOMMERCE_PERFORMANCE_RESULTS_DIRECTORY = $resolvedResultsDirectory

$arguments = @(
    "test",
    "tests/ECommerceBackend.Tests/ECommerceBackend.Tests.csproj",
    "--configuration", $Configuration,
    "--filter", "Category=SqlServerPerformance",
    "--logger", "console;verbosity=normal"
)

if ($NoBuild) {
    $arguments += "--no-build"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Performance tests failed with exit code $LASTEXITCODE."
}
