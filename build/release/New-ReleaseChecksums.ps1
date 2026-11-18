<#
.SYNOPSIS
  Writes <Directory>/SHA256SUMS.txt over the release assets of -Version (two zips, two MSIs), after checking
  that exactly those files are present. Run on the final (signed, if signing is on) files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

$expected = foreach ($kind in 'msi', 'zip') { foreach ($arch in 'x64', 'arm64') { Get-ReleaseAssetName -Kind $kind -Architecture $arch -Version $Version } }
$present = @(Get-ChildItem -LiteralPath $Directory -File | Where-Object Extension -in '.zip', '.msi' | ForEach-Object Name)
$missing = @($expected | Where-Object { $_ -cnotin $present })
$unexpected = @($present | Where-Object { $_ -cnotin $expected })
if ($missing.Count -gt 0) { throw "Release assets missing: $($missing -join ', ')." }
if ($unexpected.Count -gt 0) { throw "Unexpected files among the release assets: $($unexpected -join ', ')." }

$entries = foreach ($name in $expected) {
    [pscustomobject]@{ Name = $name; Hash = (Get-FileHash -LiteralPath (Join-Path $Directory $name) -Algorithm SHA256).Hash }
}
$text = Format-Sha256Sums $entries
# LF line endings and no BOM, so `sha256sum --check SHA256SUMS.txt` works too.
[System.IO.File]::WriteAllText((Join-Path $Directory 'SHA256SUMS.txt'), $text)
Write-Host $text
