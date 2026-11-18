<#
.SYNOPSIS
  Zips a release payload (New-ReleasePayload.ps1, possibly signed since) as
  WinXCornersPlus-<version>-win-<arch>.zip and checks what went in.

.DESCRIPTION
  Re-checks the version stamp first (signing must not have swapped files), then re-reads the zip it wrote
  rather than trusting the folder: it must contain WinXCornersPlus.exe and never a .pdb or settings.json.
  Writes zip_name to $GITHUB_OUTPUT. Checksums are written once over all release files by
  New-ReleaseChecksums.ps1.

.EXAMPLE
  ./build/release/New-ReleasePackage.ps1 -PayloadDirectory payload -OutputDirectory dist -Version 1.5.0 -NumericVersion 1.5.0.0 -Architecture x64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PayloadDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $NumericVersion,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Architecture
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleaseFiles.psm1') -Force
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
Assert-ReleaseVersionStamp -Directory $payload -Version $Version -NumericVersion $NumericVersion

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$zipName = Get-ReleaseAssetName -Kind zip -Architecture $Architecture -Version $Version
$zipPath = Join-Path $output $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($payload, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try { $entries = @($zip.Entries | ForEach-Object FullName) } finally { $zip.Dispose() }
Write-Host "Zip contents:"; $entries | ForEach-Object { Write-Host "  $_" }
foreach ($required in 'WinXCornersPlus.exe', 'WinXCornersPlus.dll', 'LICENSE.txt') {
    if ($required -notin $entries) { throw "The zip does not contain $required." }
}
$forbidden = @($entries | Where-Object { $_ -like '*.pdb' -or ($_ -split '[\\/]')[-1] -ieq 'settings.json' })
if ($forbidden.Count -gt 0) { throw "The zip contains files users must not get: $($forbidden -join ', ')" }

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "SHA-256 $hash  $zipName"
if ($env:GITHUB_OUTPUT) { Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "zip_name=$zipName" -Encoding utf8 }
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value @(
        "## ``$zipName``", '', "Version stamp verified: ``$Version`` (file version ``$NumericVersion``). SHA-256 ``$hash``", '',
        '```', ($entries -join "`n"), '```', '')
}
