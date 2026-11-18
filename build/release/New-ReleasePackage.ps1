<#
.SYNOPSIS
  Verifies the version stamp of a `dotnet publish` output and packages it as
  WinXCornersPlus-<version>-win-x64.zip plus SHA256SUMS.txt.

.DESCRIPTION
  Fails if WinXCornersPlus.exe / WinXCornersPlus.dll do not carry exactly the computed version
  (FileVersion = X.Y.Z.0, ProductVersion = the SemVer version, optionally +commit metadata), so a
  release can never ship with a stale or hand-edited number. The zip holds only what a user needs:
  no debug symbols and never a settings.json.

.EXAMPLE
  ./build/release/New-ReleasePackage.ps1 -PublishDirectory publish -OutputDirectory dist -Version 1.5.0 -NumericVersion 1.5.0.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PublishDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $NumericVersion,
    [string] $LicenseFile = (Join-Path $PSScriptRoot '../../LICENSE')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

if ($Version -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?\z') { throw "Version '$Version' is not a release version." }
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path

# --- 1. Verify the version stamp ----------------------------------------------------------------
$problems = [System.Collections.Generic.List[string]]::new()
foreach ($name in 'WinXCornersPlus.exe', 'WinXCornersPlus.dll') {
    $path = Join-Path $publish $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $problems.Add("$name is missing from the publish output."); continue }
    $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
    Write-Host ("{0}: FileVersion={1} ProductVersion={2}" -f $name, $info.FileVersion, $info.ProductVersion)
    foreach ($problem in (Test-VersionStamp -FileName $name -ExpectedVersion $Version -ExpectedNumericVersion $NumericVersion `
                -FileVersion $info.FileVersion -ProductVersion $info.ProductVersion)) {
        $problems.Add($problem)
    }
}
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    throw "Version stamp verification failed ($($problems.Count) problem(s))."
}
Write-Host "Version stamp verified: $Version ($NumericVersion)."

# --- 2. Stage the payload -------------------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$staging = Join-Path $output 'staging'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

$relative = @(Get-ChildItem -LiteralPath $publish -Recurse -File |
    ForEach-Object { [System.IO.Path]::GetRelativePath($publish, $_.FullName) } | Sort-Object)
$payload = @(Select-ReleasePayload $relative)
foreach ($file in $payload) {
    $target = Join-Path $staging $file
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $publish $file) -Destination $target
}
# MIT requires the notice to travel with the binaries.
Copy-Item -LiteralPath $LicenseFile -Destination (Join-Path $staging 'LICENSE.txt')
$excluded = @($relative | Where-Object { $_ -notin $payload })
if ($excluded.Count -gt 0) { Write-Host "Left out of the zip: $($excluded -join ', ')" }

# --- 3. Zip and checksum ----------------------------------------------------------------------------
$zipName = "WinXCornersPlus-$Version-win-x64.zip"
$zipPath = Join-Path $output $zipName
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $staging -Recurse -Force

# Re-read the zip we just wrote rather than trusting the staging folder.
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try { $entries = @($zip.Entries | ForEach-Object FullName) } finally { $zip.Dispose() }
Write-Host "Zip contents:"; $entries | ForEach-Object { Write-Host "  $_" }
if ('WinXCornersPlus.exe' -notin $entries) { throw 'The zip does not contain WinXCornersPlus.exe.' }
$forbidden = @($entries | Where-Object { $_ -like '*.pdb' -or ($_ -split '[\\/]')[-1] -ieq 'settings.json' })
if ($forbidden.Count -gt 0) { throw "The zip contains files users must not get: $($forbidden -join ', ')" }

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
# sha256sum format, LF line ending, so `sha256sum -c SHA256SUMS.txt` works too.
[System.IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), "$hash  $zipName`n")
Write-Host "SHA-256 $hash  $zipName"

if ($env:GITHUB_OUTPUT) {
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value @("zip_name=$zipName", "sha256=$hash") -Encoding utf8
}
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value @(
        "## Package ``$zipName``", '', "Version stamp verified: ``$Version`` (file version ``$NumericVersion``).", '',
        "SHA-256: ``$hash``", '', '```', ($entries -join "`n"), '```', '')
}
