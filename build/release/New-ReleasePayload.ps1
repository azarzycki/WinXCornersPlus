<#
.SYNOPSIS
  Verifies the version stamp of a `dotnet publish` output and copies what users get into -PayloadDirectory:
  the files that go, unchanged, into both the zip and the MSI (and that the optional signing stage signs).

.DESCRIPTION
  Fails if WinXCornersPlus.exe / WinXCornersPlus.dll do not carry exactly the computed version, so a release
  can never ship with a stale or hand-edited number. Leaves out debug symbols and any settings.json, and adds
  LICENSE.txt (MIT requires the notice to travel with the binaries).

.EXAMPLE
  ./build/release/New-ReleasePayload.ps1 -PublishDirectory publish -PayloadDirectory payload -Version 1.5.0 -NumericVersion 1.5.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $PayloadDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $NumericVersion,
    [string] $LicenseFile = (Join-Path $PSScriptRoot '../../LICENSE')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ReleaseFiles.psm1') -Force

if ($Version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?\z') { throw "Version '$Version' is not a release version." }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path

Assert-ReleaseVersionStamp -Directory $publish -Version $Version -NumericVersion $NumericVersion

if (Test-Path -LiteralPath $PayloadDirectory) { Remove-Item -LiteralPath $PayloadDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $PayloadDirectory | Out-Null
$payloadRoot = (Resolve-Path -LiteralPath $PayloadDirectory).Path

$relative = @(Get-ChildItem -LiteralPath $publish -Recurse -File |
    ForEach-Object { [System.IO.Path]::GetRelativePath($publish, $_.FullName) } | Sort-Object)
$payload = @(Select-ReleasePayload $relative)
foreach ($file in $payload) {
    $target = Join-Path $payloadRoot $file
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $publish $file) -Destination $target
}
Copy-Item -LiteralPath $LicenseFile -Destination (Join-Path $payloadRoot 'LICENSE.txt')

$excluded = @($relative | Where-Object { $_ -notin $payload })
if ($excluded.Count -gt 0) { Write-Host "Left out: $($excluded -join ', ')" }
Write-Host 'Payload:'
Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | ForEach-Object { Write-Host ("  {0,10:N0}  {1}" -f $_.Length, [System.IO.Path]::GetRelativePath($payloadRoot, $_.FullName)) }
