<#
.SYNOPSIS
  Re-hashes every file listed in <Directory>/SHA256SUMS.txt and fails on any mismatch or missing file.
  Run after downloading the build artifact, so what gets published is exactly what was built.
#>
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $Directory)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

$sums = Join-Path $Directory 'SHA256SUMS.txt'
$entries = @(ConvertFrom-Sha256Sums ([System.IO.File]::ReadAllText($sums)))
foreach ($entry in $entries) {
    $path = Join-Path $Directory $entry.Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "$($entry.Name) is listed in SHA256SUMS.txt but missing." }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -cne $entry.Hash) { throw "Checksum mismatch for $($entry.Name): expected $($entry.Hash), got $actual." }
    Write-Host "OK  $actual  $($entry.Name)"
}
