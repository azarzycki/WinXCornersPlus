<#
.SYNOPSIS
  Writes test counts, the names (and reasons) of skipped and failed tests, and the coverage of a Cobertura report to
  the console and to $GITHUB_STEP_SUMMARY. Never gates: the test step itself fails the job on a failed test.
.EXAMPLE
  ./build/Write-TestSummary.ps1 -ResultsDirectory TestResults -Title 'App tests (windows-2025)'
#>
param(
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [string] $Title = 'Test results'
)

$ErrorActionPreference = 'Stop'
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("### $Title")
$os = [Environment]::OSVersion.Version
$osArch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$processArch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
# The tests run in the dotnet host's process; its runtime identifier gives that process's architecture.
$rid = (& dotnet --info 2>$null | Select-String -Pattern '^\s*RID:\s*(\S+)' | Select-Object -First 1).Matches.Groups[1].Value
$lines.Add("Windows build $($os.Build) ($os); OS architecture $osArch; dotnet host RID $rid (test process architecture); summary process $processArch")

$trxFiles = @(Get-ChildItem -Path $ResultsDirectory -Recurse -Filter '*.trx' -ErrorAction SilentlyContinue)
if ($trxFiles.Count -eq 0) {
    $lines.Add('No .trx file was produced (the tests did not run).')
}

foreach ($trx in $trxFiles) {
    [xml] $xml = Get-Content -LiteralPath $trx.FullName
    $ns = @{ t = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010' }
    $results = @(Select-Xml -Xml $xml -XPath '//t:UnitTestResult' -Namespace $ns | ForEach-Object { $_.Node })
    $passed = @($results | Where-Object { $_.outcome -eq 'Passed' })
    $failed = @($results | Where-Object { $_.outcome -eq 'Failed' })
    $skipped = @($results | Where-Object { $_.outcome -eq 'NotExecuted' })
    $lines.Add('')
    $lines.Add("**$($trx.Name)**: $($passed.Count) passed, $($failed.Count) failed, $($skipped.Count) skipped (total $($results.Count))")

    function Get-Reason($node) {
        $message = $node.Output.ErrorInfo.Message
        if (-not $message) { $message = $node.Output.StdOut }
        if (-not $message) { return '' }
        return (($message -split "`r?`n") | Where-Object { $_.Trim() } | Select-Object -First 1).Trim()
    }

    if ($skipped.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Skipped:')
        foreach ($node in $skipped) { $lines.Add("- ``$($node.testName)``: $(Get-Reason $node)") }
    }

    if ($failed.Count -gt 0) {
        $lines.Add('')
        $lines.Add('Failed:')
        foreach ($node in $failed) { $lines.Add("- ``$($node.testName)``: $(Get-Reason $node)") }
    }
}

$report = Get-ChildItem -Path $ResultsDirectory -Recurse -Filter 'coverage.cobertura.xml' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($report) {
    [xml] $coverage = Get-Content -LiteralPath $report.FullName
    $culture = [System.Globalization.CultureInfo]::InvariantCulture
    $line = [double]::Parse($coverage.coverage.'line-rate', $culture) * 100
    $branch = [double]::Parse($coverage.coverage.'branch-rate', $culture) * 100
    $lines.Add('')
    $lines.Add(("App assembly coverage from these tests (reported, not gated): {0:N1}% of lines, {1:N1}% of branches" -f $line, $branch))
}

$lines | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines -join "`n")
}
