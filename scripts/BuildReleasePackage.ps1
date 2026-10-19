[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$MigrationArtifactsDirectory,

    [string]$SourceRevision = 'local-working-tree'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'src/ECommerceBackend/ECommerceBackend.csproj'

function Resolve-RepositoryPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

$resolvedOutputDirectory = Resolve-RepositoryPath $OutputDirectory
$resolvedMigrationDirectory = Resolve-RepositoryPath $MigrationArtifactsDirectory

if (-not (Test-Path -LiteralPath $resolvedMigrationDirectory -PathType Container)) {
    throw "Migration artifacts directory was not found at '$resolvedMigrationDirectory'."
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    if (Get-ChildItem -LiteralPath $resolvedOutputDirectory -Force | Select-Object -First 1) {
        throw "Release output directory must be empty: '$resolvedOutputDirectory'."
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutputDirectory | Out-Null
}

$applicationDirectory = Join-Path $resolvedOutputDirectory 'app'
$databaseDirectory = Join-Path $resolvedOutputDirectory 'database'
New-Item -ItemType Directory -Path $applicationDirectory | Out-Null
New-Item -ItemType Directory -Path $databaseDirectory | Out-Null

& dotnet publish $projectPath `
    --configuration Release `
    --no-build `
    --no-restore `
    --output $applicationDirectory `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed while building the release package.'
}

$migrationManifestPath = Join-Path $resolvedMigrationDirectory 'migration-manifest.json'
if (-not (Test-Path -LiteralPath $migrationManifestPath -PathType Leaf)) {
    throw "Migration manifest was not found at '$migrationManifestPath'."
}

$migrationManifest = Get-Content -LiteralPath $migrationManifestPath -Raw | ConvertFrom-Json
if ($migrationManifest.artifacts.Count -ne 2) {
    throw 'Migration manifest must contain exactly two SQL artifacts.'
}

foreach ($artifact in $migrationManifest.artifacts) {
    if ($artifact.file -notin @('migrate-up.sql', 'rollback-last.sql')) {
        throw "Unexpected migration artifact '$($artifact.file)'."
    }

    $sourcePath = Join-Path $resolvedMigrationDirectory $artifact.file
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Migration artifact was not found at '$sourcePath'."
    }

    $actualHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $artifact.sha256) {
        throw "Checksum verification failed for migration artifact '$($artifact.file)'."
    }

    Copy-Item -LiteralPath $sourcePath -Destination $databaseDirectory
}
Copy-Item -LiteralPath $migrationManifestPath -Destination $databaseDirectory

$forbiddenFiles = @(
    Get-ChildItem -LiteralPath $applicationDirectory -Recurse -File |
        Where-Object {
            $_.Name -like 'appsettings.Local*.json' -or
            $_.Name -like 'appsettings.Development*.json' -or
            $_.Name -eq 'appsettings.Production.example.json' -or
            $_.Extension -in @('.user', '.suo')
        }
)
if ($forbiddenFiles.Count -gt 0) {
    throw "Forbidden files were found in publish output: $($forbiddenFiles.Name -join ', ')."
}

foreach ($requiredFile in @(
    (Join-Path $applicationDirectory 'ECommerceBackend.dll'),
    (Join-Path $applicationDirectory 'appsettings.json'),
    (Join-Path $databaseDirectory 'migrate-up.sql'),
    (Join-Path $databaseDirectory 'rollback-last.sql')
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required release file was not found at '$requiredFile'."
    }
}

$files = @(
    Get-ChildItem -LiteralPath $applicationDirectory, $databaseDirectory -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($resolvedOutputDirectory.Length).TrimStart('\', '/')
            [ordered]@{
                path = $relativePath.Replace('\', '/')
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)

$releaseManifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    sourceRevision = $SourceRevision
    entryPoint = 'app/ECommerceBackend.dll'
    targetFramework = 'net8.0'
    latestMigration = $migrationManifest.latestMigration
    previousMigration = $migrationManifest.previousMigration
    files = $files
}
$releaseManifestPath = Join-Path $resolvedOutputDirectory 'release-manifest.json'
$releaseManifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath $releaseManifestPath -Encoding utf8

$archivePath = Join-Path $resolvedOutputDirectory 'ECommerceBackend-release.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archiveStream = [System.IO.File]::Open(
    $archivePath,
    [System.IO.FileMode]::CreateNew,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
try {
    $archive = [System.IO.Compression.ZipArchive]::new(
        $archiveStream,
        [System.IO.Compression.ZipArchiveMode]::Create,
        $false)
    try {
        $archiveFiles = @(
            Get-ChildItem -LiteralPath $applicationDirectory, $databaseDirectory -Recurse -File
        ) + @(Get-Item -LiteralPath $releaseManifestPath)

        foreach ($file in $archiveFiles) {
            $entryName = $file.FullName.Substring($resolvedOutputDirectory.Length).TrimStart('\', '/').Replace('\', '/')
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $file.FullName,
                $entryName,
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $archiveStream.Dispose()
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
"$archiveHash  ECommerceBackend-release.zip" |
    Set-Content -LiteralPath (Join-Path $resolvedOutputDirectory 'ECommerceBackend-release.sha256') `
        -Encoding ascii

Write-Host "Release package created at '$archivePath'."
Write-Host "Source revision: $SourceRevision"
Write-Host "Latest migration: $($migrationManifest.latestMigration)"
