[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseDirectory,

    [switch]$SmokeTest,

    [ValidateRange(5, 120)]
    [int]$StartupTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$resolvedReleaseDirectory = [System.IO.Path]::GetFullPath($ReleaseDirectory)
$archivePath = Join-Path $resolvedReleaseDirectory 'ECommerceBackend-release.zip'
$checksumPath = Join-Path $resolvedReleaseDirectory 'ECommerceBackend-release.sha256'

foreach ($requiredPath in @($archivePath, $checksumPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required release artifact was not found at '$requiredPath'."
    }
}

$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
if ($checksumLine -notmatch '^(?<Hash>[0-9a-fA-F]{64})\s+ECommerceBackend-release\.zip$') {
    throw 'Release checksum file has an invalid format.'
}

$expectedArchiveHash = $Matches.Hash.ToLowerInvariant()
$actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualArchiveHash -ne $expectedArchiveHash) {
    throw 'Release archive checksum verification failed.'
}

$extractionDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "ECommerceBackendRelease_$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $extractionDirectory | Out-Null

try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $forbiddenPrefixes = @(
            'app/Application/',
            'app/Domain/',
            'app/Infrastructure/',
            'app/ECommerceBackend.Tests/',
            'app/ECommerceBackend.UnitTests/',
            'app/ECommerceBackend.IntegrationTests/',
            'app/TestResults/',
            'app/PerformanceResults/',
            'app/ReleasePackage/',
            'app/MigrationArtifacts/',
            'app/docs/',
            'app/scripts/',
            'app/DataProtectionKeys/',
            'app/logs/',
            'app/uploads/'
        )
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($entryPath) -or
                $entryPath.StartsWith('/') -or
                $entryPath -match '(^|/)\.\.(/|$)') {
                throw "Release archive contains an unsafe entry '$entryPath'."
            }

            if ($forbiddenPrefixes |
                Where-Object { $entryPath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }) {
                throw "Release archive contains source or runtime data '$entryPath'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractionDirectory)
    $manifestPath = Join-Path $extractionDirectory 'release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw 'Release manifest is missing from the archive.'
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or
        $manifest.targetFramework -ne 'net8.0' -or
        $manifest.entryPoint -ne 'app/ECommerceBackend.dll' -or
        [string]::IsNullOrWhiteSpace($manifest.latestMigration)) {
        throw 'Release manifest metadata is invalid.'
    }

    $manifestFiles = @($manifest.files)
    if ($manifestFiles.Count -eq 0) {
        throw 'Release manifest does not contain file checksums.'
    }

    foreach ($file in $manifestFiles) {
        $relativePath = ([string]$file.path).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        $filePath = Join-Path $extractionDirectory $relativePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            throw "Manifest file '$($file.path)' is missing from the archive."
        }

        $actualLength = (Get-Item -LiteralPath $filePath).Length
        if ($actualLength -ne [long]$file.length) {
            throw "Length verification failed for '$($file.path)'."
        }

        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne [string]$file.sha256) {
            throw "Checksum verification failed for '$($file.path)'."
        }
    }

    $actualArchiveFiles = @(
        Get-ChildItem -LiteralPath $extractionDirectory -Recurse -File |
            ForEach-Object {
                $_.FullName.Substring($extractionDirectory.Length).TrimStart('\', '/').Replace('\', '/')
            }
    )
    $expectedArchiveFiles = @($manifestFiles | ForEach-Object { [string]$_.path }) +
        @('release-manifest.json')
    $unexpectedFiles = @($actualArchiveFiles | Where-Object { $_ -notin $expectedArchiveFiles })
    $missingFiles = @($expectedArchiveFiles | Where-Object { $_ -notin $actualArchiveFiles })
    if ($unexpectedFiles.Count -gt 0 -or $missingFiles.Count -gt 0) {
        throw "Archive contents do not match the manifest. Missing: '$($missingFiles -join ', ')'; unexpected: '$($unexpectedFiles -join ', ')'."
    }

    if ($SmokeTest) {
        $listener = [System.Net.Sockets.TcpListener]::new(
            [System.Net.IPAddress]::Loopback,
            0)
        $listener.Start()
        $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
        $listener.Stop()

        $applicationDirectory = Join-Path $extractionDirectory 'app'
        $stdoutPath = Join-Path $extractionDirectory 'smoke-stdout.log'
        $stderrPath = Join-Path $extractionDirectory 'smoke-stderr.log'
        $environmentOverrides = [ordered]@{
            ASPNETCORE_ENVIRONMENT = 'Development'
            Jwt__Key = "release-smoke-$('x' * 48)"
            AdminBootstrap__Enabled = 'false'
            Outbox__Enabled = 'false'
            OrderLifecycle__ExpirationEnabled = 'false'
            DataRetention__AutomaticProcessingEnabled = 'false'
            Notifications__Smtp__Enabled = 'false'
            Observability__Enabled = 'false'
            Swagger__Enabled = 'false'
        }
        $originalEnvironment = @{}
        foreach ($name in $environmentOverrides.Keys) {
            $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable(
                $name,
                $environmentOverrides[$name],
                'Process')
        }

        $process = $null
        try {
            $startParameters = @{
                FilePath = 'dotnet'
                ArgumentList = @('ECommerceBackend.dll', '--urls', "http://127.0.0.1:$port")
                WorkingDirectory = $applicationDirectory
                RedirectStandardOutput = $stdoutPath
                RedirectStandardError = $stderrPath
                PassThru = $true
            }
            if ($env:OS -eq 'Windows_NT') {
                $startParameters.WindowStyle = 'Hidden'
            }

            $process = Start-Process @startParameters
            $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
            $healthy = $false
            while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
                try {
                    $response = Invoke-WebRequest `
                        -Uri "http://127.0.0.1:$port/health/live" `
                        -UseBasicParsing `
                        -TimeoutSec 2
                    if ($response.StatusCode -eq 200) {
                        $healthy = $true
                        break
                    }
                }
                catch {
                    Start-Sleep -Milliseconds 250
                }
            }

            if (-not $healthy) {
                $stderr = if (Test-Path -LiteralPath $stderrPath) {
                    Get-Content -LiteralPath $stderrPath -Raw
                }
                else {
                    ''
                }
                throw "Published application did not pass the live health smoke test. $stderr"
            }
        }
        finally {
            if ($null -ne $process -and -not $process.HasExited) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }

            foreach ($name in $environmentOverrides.Keys) {
                [Environment]::SetEnvironmentVariable(
                    $name,
                    $originalEnvironment[$name],
                    'Process')
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $extractionDirectory) {
        Remove-Item -LiteralPath $extractionDirectory -Recurse -Force
    }
}

Write-Host "Release package verification succeeded for '$archivePath'."
