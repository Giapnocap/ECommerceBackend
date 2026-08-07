[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$allowedFixturePaths = @(
    'scripts/TestRepositorySecrets.ps1'
)
$allowedBCryptFixturePaths = @(
    'scripts/SeedDemoData.sql'
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
    JwtToken = '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\b'
    BCryptHash = '\$2[abxy]\$\d{2}\$[./A-Za-z0-9]{53}'
    JsonCredential = '(?i)"(?:Password|Secret|ApiKey|AccessToken|PrivateKey|Key)"[ \t]*:[ \t]*"(?<value>[^"]+)"'
    HttpCredential = '(?im)^[ \t]*@[A-Z0-9_]*(?:PASSWORD|SECRET|TOKEN|API[_-]?KEY|PRIVATE[_-]?KEY|JWT[_-]?KEY)[A-Z0-9_]*[ \t]*=[ \t]*["'']?(?<value>[^"''#\r\n]+)'
    NamedCredential = '(?im)^[ \t]*(?:[A-Z0-9_]*(?:PASSWORD|SECRET|API_KEY|ACCESS_TOKEN|PRIVATE_KEY|JWT_KEY)[A-Z0-9_]*)[ \t]*:[ \t]*["'']?(?<value>[^"''#\r\n]+)'
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
        if ($pattern.Key -eq 'BCryptHash' -and
            $normalizedPath -in $allowedBCryptFixturePaths) {
            continue
        }

        foreach ($match in [regex]::Matches($content, $pattern.Value)) {
            $value = if ($match.Groups['value'].Success) {
                $match.Groups['value'].Value
            }
            else {
                $match.Value
            }

            $normalizedValue = $value.Trim()
            $isPlaceholder = [string]::IsNullOrWhiteSpace($normalizedValue) `
                -or $normalizedValue -match '^(?:YOUR_|replace-with|change-me|test-|example|placeholder|generate-a-|set-a-)' `
                -or $normalizedValue -match '^<.+>$' `
                -or $normalizedValue -match '^\{\{[A-Za-z0-9_.-]+\}\}$' `
                -or $normalizedValue -match '\$\{\{[^}]+\}\}'
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
