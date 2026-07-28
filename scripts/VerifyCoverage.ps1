[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ReportDirectory,

    [ValidateRange(0, 100)]
    [double]$MinimumLineRate = 60,

    [ValidateRange(0, 100)]
    [double]$MinimumBranchRate = 40
)

$reports = @(
    Get-ChildItem -LiteralPath $ReportDirectory -Filter 'coverage.cobertura.xml' -Recurse -File
)
if ($reports.Count -eq 0) {
    throw "No Cobertura reports were found under '$ReportDirectory'."
}

$lineCoverage = @{}
$branchCoverage = @{}

foreach ($report in $reports) {
    [xml]$coverage = Get-Content -LiteralPath $report.FullName
    foreach ($class in @($coverage.coverage.packages.package.classes.class)) {
        $fileName = ([string]$class.filename).Replace('\', '/').ToLowerInvariant()
        foreach ($line in @($class.lines.line)) {
            $key = "$fileName`:$($line.number)"
            $hits = [int]$line.hits

            if (-not $lineCoverage.ContainsKey($key) -or $hits -gt $lineCoverage[$key]) {
                $lineCoverage[$key] = $hits
            }

            if (-not ([string]$line.branch).Equals(
                'True',
                [StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            $conditionCoverage = [regex]::Match(
                [string]$line.'condition-coverage',
                '\((?<Covered>\d+)/(?<Total>\d+)\)')
            if (-not $conditionCoverage.Success) {
                continue
            }

            $covered = [int]$conditionCoverage.Groups['Covered'].Value
            $total = [int]$conditionCoverage.Groups['Total'].Value
            if (-not $branchCoverage.ContainsKey($key)) {
                $branchCoverage[$key] = [pscustomobject]@{
                    Covered = $covered
                    Total = $total
                }
                continue
            }

            # Cobertura does not identify individual branch paths, so merge duplicate lines conservatively.
            $current = $branchCoverage[$key]
            $current.Covered = [Math]::Max($current.Covered, $covered)
            $current.Total = [Math]::Max($current.Total, $total)
        }
    }
}

if ($lineCoverage.Count -eq 0) {
    throw "Cobertura reports under '$ReportDirectory' do not contain executable lines."
}

$coveredLines = @($lineCoverage.Values | Where-Object { $_ -gt 0 }).Count
$lineRate = $coveredLines / $lineCoverage.Count * 100
$coveredBranches = (
    $branchCoverage.Values |
        Measure-Object -Property Covered -Sum
).Sum
$totalBranches = (
    $branchCoverage.Values |
        Measure-Object -Property Total -Sum
).Sum
$branchRate = if ($totalBranches -gt 0) {
    $coveredBranches / $totalBranches * 100
}
else {
    100
}

Write-Host "Coverage reports merged: $($reports.Count)"
Write-Host ("Line coverage: {0:N2}% (minimum {1:N2}%)" -f $lineRate, $MinimumLineRate)
Write-Host ("Branch coverage: {0:N2}% (minimum {1:N2}%)" -f $branchRate, $MinimumBranchRate)

$failures = [System.Collections.Generic.List[string]]::new()
if ($lineRate -lt $MinimumLineRate) {
    $failures.Add(("Line coverage {0:N2}% is below {1:N2}%." -f $lineRate, $MinimumLineRate))
}

if ($branchRate -lt $MinimumBranchRate) {
    $failures.Add(("Branch coverage {0:N2}% is below {1:N2}%." -f $branchRate, $MinimumBranchRate))
}

if ($failures.Count -gt 0) {
    throw ($failures -join ' ')
}
