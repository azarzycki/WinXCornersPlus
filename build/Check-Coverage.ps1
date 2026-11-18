<#
.SYNOPSIS
  Fails the build when line or branch coverage in a Cobertura report is below a threshold.
.EXAMPLE
  ./build/Check-Coverage.ps1 -ResultsDirectory TestResults -MinimumLine 95 -MinimumBranch 90
#>
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [double] $MinimumLine = 95,
    [double] $MinimumBranch = 90
)

$ErrorActionPreference = 'Stop'

$report = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $report) {
    Write-Error "No coverage.cobertura.xml found under '$ResultsDirectory'."
}

[xml] $xml = Get-Content -LiteralPath $report.FullName
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$line = [double]::Parse($xml.coverage.'line-rate', $culture) * 100
$branch = [double]::Parse($xml.coverage.'branch-rate', $culture) * 100

$summary = "Coverage: {0:N1}% of lines (minimum {1}%), {2:N1}% of branches (minimum {3}%)" -f $line, $MinimumLine, $branch, $MinimumBranch
Write-Host $summary
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value "### $summary"
}

if ($line -lt $MinimumLine -or $branch -lt $MinimumBranch) {
    Write-Error 'Coverage is below the required minimum.'
}
