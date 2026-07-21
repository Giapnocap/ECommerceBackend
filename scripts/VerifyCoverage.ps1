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

$reports = Get-ChildItem -LiteralPath $ReportDirectory -Filter 'coverage.cobertura.xml' -Recurse -File
if ($reports.Count -ne 1) {
    throw "Expected exactly one Cobertura report under '$ReportDirectory', found $($reports.Count)."
}

[xml]$coverage = Get-Content -LiteralPath $reports[0].FullName
$lineRate = [double]$coverage.coverage.'line-rate' * 100
$branchRate = [double]$coverage.coverage.'branch-rate' * 100

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
