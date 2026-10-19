[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [string]$DotNetEfPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$migrationsProjectPath = Join-Path $repositoryRoot 'src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj'
$startupProjectPath = Join-Path $repositoryRoot 'src/ECommerceBackend.Infrastructure/ECommerceBackend.Infrastructure.csproj'
$resolvedOutputDirectory = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

if ([string]::IsNullOrWhiteSpace($DotNetEfPath)) {
    $efExecutable = 'dotnet'
    $efPrefixArguments = @('ef')
}
else {
    $efExecutable = if ([System.IO.Path]::IsPathRooted($DotNetEfPath)) {
        [System.IO.Path]::GetFullPath($DotNetEfPath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $DotNetEfPath))
    }
    $efPrefixArguments = @()
    if (-not (Test-Path -LiteralPath $efExecutable -PathType Leaf)) {
        throw "dotnet-ef executable was not found at '$efExecutable'."
    }
}

function Invoke-EfCommand {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & $efExecutable @efPrefixArguments @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet-ef failed: $($output -join [Environment]::NewLine)"
    }

    return $output
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
$commonArguments = @(
    '--project', $migrationsProjectPath,
    '--startup-project', $startupProjectPath,
    '--configuration', 'Release',
    '--no-build'
)

$pendingOutput = Invoke-EfCommand -Arguments (@(
    'migrations', 'has-pending-model-changes'
) + $commonArguments)
if (($pendingOutput -join "`n") -notmatch 'No changes have been made to the model') {
    throw "The EF model has changes that are not represented by a migration."
}

$migrationListOutput = Invoke-EfCommand -Arguments (@(
    'migrations', 'list', '--no-connect'
) + $commonArguments)
$migrationIds = @(
    $migrationListOutput |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -match '^\d{14}_[A-Za-z0-9_]+' } |
        ForEach-Object { ($_ -split '\s+')[0] }
)
if ($migrationIds.Count -lt 2) {
    throw "At least two migrations are required to build forward and rollback artifacts."
}

$latestMigration = $migrationIds[-1]
$previousMigration = $migrationIds[-2]
$forwardScript = Join-Path $resolvedOutputDirectory 'migrate-up.sql'
$rollbackScript = Join-Path $resolvedOutputDirectory 'rollback-last.sql'

Invoke-EfCommand -Arguments (@(
    'migrations', 'script',
    '--idempotent',
    '--output', $forwardScript
) + $commonArguments) | Out-Null

Invoke-EfCommand -Arguments (@(
    'migrations', 'script',
    $latestMigration,
    $previousMigration,
    '--output', $rollbackScript
) + $commonArguments) | Out-Null

foreach ($artifact in @($forwardScript, $rollbackScript)) {
    $file = Get-Item -LiteralPath $artifact
    if ($file.Length -eq 0) {
        throw "Migration artifact '$($file.FullName)' is empty."
    }
}

$manifest = [ordered]@{
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    latestMigration = $latestMigration
    previousMigration = $previousMigration
    requiresDatabaseBackupBeforeApply = $true
    requiresRestoreDrillBeforeProduction = $true
    artifacts = @(
        [ordered]@{
            file = 'migrate-up.sql'
            sha256 = (Get-FileHash -LiteralPath $forwardScript -Algorithm SHA256).Hash.ToLowerInvariant()
        },
        [ordered]@{
            file = 'rollback-last.sql'
            sha256 = (Get-FileHash -LiteralPath $rollbackScript -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    )
}
$manifestPath = Join-Path $resolvedOutputDirectory 'migration-manifest.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Migration artifacts created in '$resolvedOutputDirectory'."
Write-Host "Latest migration: $latestMigration"
Write-Host "Rollback target: $previousMigration"
