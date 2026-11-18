<#
.SYNOPSIS
  Reads git tags, commit messages and the csproj base version, computes the next release version
  (see ReleaseTools.psm1 for the rules) and writes it to $GITHUB_OUTPUT and the job summary.

.DESCRIPTION
  This is the only release script that reads git. It needs the full history and all tags
  (actions/checkout with fetch-depth: 0) and refuses to run in a shallow clone, where it would
  silently miss tags and compute a wrong version.

.EXAMPLE
  ./build/release/Get-ReleaseVersion.ps1 -ProjectFile src/WinXCorners.App/WinXCorners.App.csproj
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectFile,
    [string] $RepositoryRoot = '.',
    [AllowEmptyString()] [string] $Bump = 'auto',
    [switch] $Prerelease,
    [int] $RunNumber = 0,
    [string] $OutputFile = $env:GITHUB_OUTPUT,
    [string] $SummaryFile = $env:GITHUB_STEP_SUMMARY
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# A push event has no inputs; treat "not given" as auto.
if ([string]::IsNullOrWhiteSpace($Bump)) { $Bump = 'auto' }
if ($Bump -cnotin 'auto', 'patch', 'minor', 'major') { throw "Bump must be auto, patch, minor or major (got '$Bump')." }

# Arguments are passed as one explicit array: PowerShell would otherwise swallow a literal "--".
function Invoke-Git {
    param([string[]] $Arguments)
    $output = & git -C $RepositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    return $output
}

# --- Base version from the csproj -------------------------------------------------------------
[xml] $project = Get-Content -LiteralPath $ProjectFile -Raw
$versionNodes = @($project.SelectNodes('/Project/PropertyGroup/Version'))
if ($versionNodes.Count -ne 1) {
    throw "Expected exactly one <Version> in $ProjectFile, found $($versionNodes.Count)."
}
$baseVersion = $versionNodes[0].InnerText

# --- Facts from git ---------------------------------------------------------------------------
if ((Invoke-Git @('rev-parse', '--is-shallow-repository')) -ne 'false') {
    throw 'The repository is a shallow clone; fetch the full history and tags (fetch-depth: 0).'
}
[string[]] $tags = @(Invoke-Git @('tag', '--list'))

$latest = Get-LatestStableRelease $tags
[string[]] $messages = @()
if ($null -ne $latest) {
    # NUL-separated full messages of the commits reachable from HEAD but not from the release tag.
    # The tag name already passed the strict release-tag pattern, so it is safe as an argument.
    $raw = (Invoke-Git @('log', '--format=%B%x00', "refs/tags/$($latest.Tag)..HEAD", '--')) -join "`n"
    $messages = @($raw -split "`0" | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
}

$result = Get-NextReleaseVersion -Tags $tags -CommitMessages $messages -BaseVersion $baseVersion `
    -Bump $Bump -Prerelease:$Prerelease -RunNumber $RunNumber

# --- Report -----------------------------------------------------------------------------------
Write-Host "Next version: $($result.Version) (tag $($result.Tag))"
Write-Host $result.Reason
Write-Host "Commits considered since the last release: $($messages.Count)"

if ($OutputFile) {
    # Every value is validated by the module (digits, dots, 'rc', 'v'), so none can contain a newline.
    $previous = if ($null -eq $result.PreviousTag) { '' } else { $result.PreviousTag }
    $lines = @(
        "version=$($result.Version)"
        "tag=$($result.Tag)"
        "numeric_version=$($result.NumericVersion)"
        "prerelease=$($result.IsPrerelease.ToString().ToLowerInvariant())"
        "bump=$($result.Bump)"
        "previous_tag=$previous"
    )
    Add-Content -LiteralPath $OutputFile -Value $lines -Encoding utf8
}

if ($SummaryFile) {
    $previousText = if ($null -eq $result.PreviousTag) { '(none)' } else { "``$($result.PreviousTag)``" }
    $summary = @(
        "## Release version: ``$($result.Version)``"
        ''
        '| | |'
        '|---|---|'
        "| Tag | ``$($result.Tag)`` |"
        "| Previous stable release | $previousText |"
        "| Base version (csproj) | ``$baseVersion`` |"
        "| Requested bump | ``$Bump`` |"
        "| Decided by | ``$($result.Bump)`` |"
        "| Commits since previous release | $($messages.Count) |"
        "| Prerelease | $($result.IsPrerelease) |"
        ''
        $result.Reason
        ''
    )
    Add-Content -LiteralPath $SummaryFile -Value $summary -Encoding utf8
}
