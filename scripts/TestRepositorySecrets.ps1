[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
# CI SQL credentials belong only to disposable localhost containers and are intentionally
# reviewable fixtures. Application configuration and every other tracked path remain scanned.
$allowedFixturePaths = @(
    '.github/workflows/ci.yml',
    '.github/workflows/performance.yml',
    'README.md',
    'src/ECommerceBackend/ECommerceBackend.http',
    'scripts/SeedDemoData.sql',
    'scripts/TestRepositorySecrets.ps1'
)
$allowedPrefixes = @(
    'tests/ECommerceBackend.UnitTests/',
    'tests/ECommerceBackend.IntegrationTests/'
)
$patterns = [ordered]@{
    PrivateKey = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    GitHubToken = '\bgh[pousr]_[A-Za-z0-9]{30,}\b'
    GitLabToken = '\bglpat-[A-Za-z0-9_-]{20,}\b'
    AwsAccessKey = '\bAKIA[0-9A-Z]{16}\b'
    SlackToken = '\bxox[baprs]-[A-Za-z0-9-]{20,}\b'
    StripeLiveKey = '\bsk_live_[A-Za-z0-9]{20,}\b'
    JsonCredential = '"(?:Password|Secret|ApiKey|AccessToken|PrivateKey|Key)"\s*:\s*"(?<value>[^"]+)"'
    ConnectionPassword = '(?i)(?:Password|Pwd)\s*=\s*(?<value>[^;"\s]+)'
}

$trackedFiles = @(& git -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not enumerate tracked repository files.'
}

$findings = [System.Collections.Generic.List[string]]::new()
foreach ($relativePath in $trackedFiles) {
    $normalizedPath = $relativePath.Replace('\', '/')
    $hasAllowedPrefix = $false
    foreach ($prefix in $allowedPrefixes) {
        if ($normalizedPath.StartsWith($prefix, [StringComparison]::Ordinal)) {
            $hasAllowedPrefix = $true
            break
        }
    }

    if (($normalizedPath -in $allowedFixturePaths) -or $hasAllowedPrefix) {
        continue
    }

    $fullPath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    try {
        $content = [IO.File]::ReadAllText($fullPath)
    }
    catch {
        continue
    }

    foreach ($pattern in $patterns.GetEnumerator()) {
        foreach ($match in [regex]::Matches($content, $pattern.Value)) {
            $value = if ($match.Groups['value'].Success) {
                $match.Groups['value'].Value
            }
            else {
                $match.Value
            }

            $isPlaceholder = [string]::IsNullOrWhiteSpace($value) `
                -or $value -match '^(?:YOUR_|replace-with|change-me|test-|example|placeholder|generate-a-|set-a-)' `
                -or $value -match '^<.+>$'
            if ($isPlaceholder) {
                continue
            }

            $line = 1 + ($content.Substring(0, $match.Index) -split "`n").Count - 1
            $findings.Add("$normalizedPath`:$line [$($pattern.Key)]")
        }
    }
}

if ($findings.Count -gt 0) {
    throw "Potential secrets were found in tracked files:`n$($findings -join "`n")"
}

Write-Host "No high-confidence secrets were found in tracked repository files."
