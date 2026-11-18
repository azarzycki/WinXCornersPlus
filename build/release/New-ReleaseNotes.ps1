<#
.SYNOPSIS
  Writes the release-notes body (which file is for whom, requirements, SHA-256 of every asset, how to verify,
  code-signing status) to -OutputFile, from <Directory>/SHA256SUMS.txt. GitHub's generated change list is
  appended at publish time.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Directory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $OutputFile,
    [string] $ServerUrl = 'https://github.com',
    [AllowEmptyString()] [string] $SignedBy = '',
    [switch] $Prerelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

$entries = @(ConvertFrom-Sha256Sums ([System.IO.File]::ReadAllText((Join-Path $Directory 'SHA256SUMS.txt'))))
$notes = Format-ReleaseNotes -Version $Version -Assets $entries -Repository $Repository -ServerUrl $ServerUrl `
    -SignedBy $SignedBy -Prerelease:$Prerelease
[System.IO.File]::WriteAllText($OutputFile, $notes)
Write-Host $notes
