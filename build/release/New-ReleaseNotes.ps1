<#
.SYNOPSIS
  Writes the release-notes body (download, requirements, SHA-256, how to verify, not code-signed)
  for the zip in -Directory to -OutputFile. GitHub's generated change list is appended at publish time.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $OutputFile,
    [switch] $Prerelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

$zipName = "WinXCornersPlus-$Version-win-x64.zip"
$entries = @(ConvertFrom-Sha256Sums ([System.IO.File]::ReadAllText((Join-Path $Directory 'SHA256SUMS.txt'))) |
    Where-Object Name -ceq $zipName)
if ($entries.Count -ne 1) { throw "SHA256SUMS.txt has no single entry for $zipName." }

$notes = Format-ReleaseNotes -Version $Version -ZipName $zipName -Sha256 $entries[0].Hash -Repository $Repository -Prerelease:$Prerelease
[System.IO.File]::WriteAllText($OutputFile, $notes)
Write-Host $notes
