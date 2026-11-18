<#
.SYNOPSIS
  Pure release logic for WinXCornersPlus: version computation, release gating, payload selection,
  version-stamp checks, checksum parsing and release notes.

.DESCRIPTION
  Nothing in this module touches git, the file system, the network or the environment. The scripts
  next to it (Get-ReleaseVersion.ps1, New-ReleasePackage.ps1, ...) gather the facts and call these
  functions, so every rule here can be unit-tested with Pester on any OS.
#>

Set-StrictMode -Version 3.0

# Windows stores AssemblyVersion/FileVersion components as UInt16 and reserves 65535, so a release
# version component above 65534 could not be stamped into the exe. Such tags are treated as malformed.
$script:MaxVersionComponent = 65534

# Release tags are "v" + SemVer 2.0 without build metadata. [0-9] (not \d) on purpose: .NET's \d also
# matches non-ASCII digits such as Arabic-Indic numerals. \A and \z (not ^ and $) so a trailing newline
# cannot sneak through.
$script:TagPattern = '\Av(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z'
$script:CorePattern = '\A(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)\z'

function ConvertTo-VersionComponent {
    # Returns the component as [int], or -1 if it is out of the stampable range (or absurdly long).
    param([string] $Text)
    if ($Text.Length -gt 5) { return -1 }
    $value = [int]::Parse($Text, [System.Globalization.CultureInfo]::InvariantCulture)
    if ($value -gt $script:MaxVersionComponent) { return -1 }
    return $value
}

function New-ReleaseVersionObject {
    param([int] $Major, [int] $Minor, [int] $Patch, [AllowNull()] [string] $Prerelease, [AllowNull()] [string] $Tag)
    [pscustomobject]@{
        Major      = $Major
        Minor      = $Minor
        Patch      = $Patch
        Prerelease = if ([string]::IsNullOrEmpty($Prerelease)) { $null } else { $Prerelease }
        Tag        = $Tag
        Core       = '{0}.{1}.{2}' -f $Major, $Minor, $Patch
    }
}

function ConvertFrom-ReleaseTag {
    <#
    .SYNOPSIS
      Parses a release tag such as "v1.5.0" or "v1.6.0-rc.12". Returns $null for anything else.
    .DESCRIPTION
      Never throws on bad input: malformed tags ("v1", "v1.2", "vnext", "v01.2.3", "V1.2.3",
      "v1.2.3+build", components above 65534, non-ASCII digits, whitespace) are simply not release tags.
    #>
    [CmdletBinding()]
    param([Parameter(Position = 0)] [AllowNull()] [AllowEmptyString()] [string] $Tag)

    if ([string]::IsNullOrEmpty($Tag) -or $Tag.Length -gt 256) { return $null }
    $m = [regex]::Match($Tag, $script:TagPattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $m.Success) { return $null }

    $major = ConvertTo-VersionComponent $m.Groups['major'].Value
    $minor = ConvertTo-VersionComponent $m.Groups['minor'].Value
    $patch = ConvertTo-VersionComponent $m.Groups['patch'].Value
    if ($major -lt 0 -or $minor -lt 0 -or $patch -lt 0) { return $null }

    $pre = if ($m.Groups['pre'].Success) { $m.Groups['pre'].Value } else { $null }
    return New-ReleaseVersionObject -Major $major -Minor $minor -Patch $patch -Prerelease $pre -Tag $Tag
}

function ConvertFrom-BaseVersion {
    <#
    .SYNOPSIS
      Parses the csproj <Version> ("MAJOR.MINOR.PATCH"). Throws if it is not exactly that, because a
      broken base version is a configuration error that must stop a release, unlike a stray tag.
    #>
    [CmdletBinding()]
    param([Parameter(Position = 0)] [AllowNull()] [AllowEmptyString()] [string] $Version)

    $text = if ($null -eq $Version) { '' } else { $Version.Trim() }
    $m = [regex]::Match($text, $script:CorePattern, [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if ($m.Success -and $text.Length -le 32) {
        $major = ConvertTo-VersionComponent $m.Groups['major'].Value
        $minor = ConvertTo-VersionComponent $m.Groups['minor'].Value
        $patch = ConvertTo-VersionComponent $m.Groups['patch'].Value
        if ($major -ge 0 -and $minor -ge 0 -and $patch -ge 0) {
            return New-ReleaseVersionObject -Major $major -Minor $minor -Patch $patch -Prerelease $null -Tag $null
        }
    }
    throw "The project's base <Version> '$Version' is not MAJOR.MINOR.PATCH with each part between 0 and $($script:MaxVersionComponent)."
}

function Compare-VersionCore {
    # Numeric comparison of MAJOR.MINOR.PATCH only: -1, 0 or 1.
    param($Left, $Right)
    foreach ($part in 'Major', 'Minor', 'Patch') {
        if ($Left.$part -lt $Right.$part) { return -1 }
        if ($Left.$part -gt $Right.$part) { return 1 }
    }
    return 0
}

function Get-LatestStableRelease {
    <#
    .SYNOPSIS
      Returns the parsed highest stable (non-prerelease) release tag, or $null when there is none.
      Comparison is numeric, so v1.10.0 beats v1.9.0. Malformed and prerelease tags are skipped.
    #>
    [CmdletBinding()]
    param([Parameter(Position = 0)] [AllowNull()] [AllowEmptyCollection()] [string[]] $Tags)

    $best = $null
    foreach ($tag in @($Tags)) {
        $parsed = ConvertFrom-ReleaseTag $tag
        if ($null -eq $parsed -or $null -ne $parsed.Prerelease) { continue }
        if ($null -eq $best -or (Compare-VersionCore $parsed $best) -gt 0) { $best = $parsed }
    }
    return $best
}

function Get-CommitBump {
    <#
    .SYNOPSIS
      Infers the bump level from full commit messages (subject + body) using Conventional Commits:
      'major' if any subject is "type!:" / "type(scope)!:" or any line starts with "BREAKING CHANGE:"
      (or "BREAKING-CHANGE:"); else 'minor' if any subject is "feat:" / "feat(scope):"; else 'patch'.
      Anything else, including non-conventional messages and empty history, is a patch.
    #>
    [CmdletBinding()]
    param([Parameter(Position = 0)] [AllowNull()] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $CommitMessages)

    $breakingSubject = '\A[A-Za-z][A-Za-z0-9-]*(?:\([^()\r\n]*\))?!:(?:[ \t]|\z|\r?\n)'
    # Footer token is case-sensitive per the Conventional Commits spec.
    $breakingFooter = '(?m)^BREAKING[ -]CHANGE:'
    $featSubject = '\A(?i:feat)(?:\([^()\r\n]*\))?:(?:[ \t]|\z|\r?\n)'
    $options = [System.Text.RegularExpressions.RegexOptions]::CultureInvariant

    $sawFeature = $false
    foreach ($message in @($CommitMessages)) {
        if ([string]::IsNullOrWhiteSpace($message)) { continue }
        $text = $message.TrimStart()
        if ([regex]::IsMatch($text, $breakingSubject, $options) -or
            [regex]::IsMatch($text, $breakingFooter, $options)) {
            return 'major'
        }
        if (-not $sawFeature -and [regex]::IsMatch($text, $featSubject, $options)) { $sawFeature = $true }
    }
    if ($sawFeature) { return 'minor' }
    return 'patch'
}

function Get-NextReleaseVersion {
    <#
    .SYNOPSIS
      Computes the next release version. Pure: the caller supplies every fact.
    .PARAMETER Tags
      All tag names in the repository (any names; non-release tags are ignored).
    .PARAMETER CommitMessages
      Full messages of the commits since the latest stable release tag (ignored when there is none).
    .PARAMETER BaseVersion
      The csproj <Version>: the version of the first release and a floor for every later one.
    .PARAMETER Bump
      'auto' (infer from commit messages) or a forced 'patch' / 'minor' / 'major'.
    .PARAMETER Prerelease
      Produce "X.Y.Z-rc.<RunNumber>" instead of "X.Y.Z".
    .OUTPUTS
      Version, Tag, NumericVersion (X.Y.Z.0, for AssemblyVersion/FileVersion), IsPrerelease, Bump
      (the rule that decided: initial, patch, minor, major or base), PreviousTag, Reason.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyCollection()] [string[]] $Tags = @(),
        [AllowNull()] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $CommitMessages = @(),
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $BaseVersion,
        [ValidateSet('auto', 'patch', 'minor', 'major')] [string] $Bump = 'auto',
        [switch] $Prerelease,
        [int] $RunNumber = 0
    )

    $base = ConvertFrom-BaseVersion $BaseVersion
    if ($Prerelease -and $RunNumber -lt 1) { throw 'A prerelease needs a positive run number.' }

    $latest = Get-LatestStableRelease $Tags
    if ($null -eq $latest) {
        $next = $base
        $decided = 'initial'
        $reason = "No stable release tag yet, so the first release is the project's base version $($base.Core)."
        if ($Bump -ne 'auto') { $reason += " The requested '$Bump' bump does not apply to a first release." }
    }
    else {
        $level = if ($Bump -eq 'auto') { Get-CommitBump $CommitMessages } else { $Bump }
        $why = if ($Bump -eq 'auto') {
            switch ($level) {
                'major' { 'a commit since then marks a breaking change' }
                'minor' { 'a commit since then is a feat: commit' }
                default { 'no commit since then is a feat: or breaking change' }
            }
        }
        else { "a '$Bump' bump was requested" }

        $major = $latest.Major; $minor = $latest.Minor; $patch = $latest.Patch
        switch ($level) {
            'major' { $major++; $minor = 0; $patch = 0 }
            'minor' { $minor++; $patch = 0 }
            default { $patch++ }
        }
        if ($major -gt $script:MaxVersionComponent -or $minor -gt $script:MaxVersionComponent -or $patch -gt $script:MaxVersionComponent) {
            throw "Bumping $($latest.Tag) ($level) would exceed $($script:MaxVersionComponent) in a version component."
        }
        $next = New-ReleaseVersionObject -Major $major -Minor $minor -Patch $patch -Prerelease $null -Tag $null
        $decided = $level
        $reason = "Latest stable release is $($latest.Tag); $level bump because $why -> $($next.Core)."

        if ((Compare-VersionCore $base $next) -gt 0) {
            $reason += " The project's base version $($base.Core) is higher, so it wins."
            $next = $base
            $decided = 'base'
        }
    }

    $version = if ($Prerelease) { '{0}-rc.{1}' -f $next.Core, $RunNumber } else { $next.Core }
    $tag = "v$version"
    foreach ($existing in @($Tags)) {
        if ($existing -ceq $tag) { throw "Tag $tag already exists; refusing to compute a version that was already released." }
    }

    [pscustomobject]@{
        Version        = $version
        Tag            = $tag
        NumericVersion = '{0}.0' -f $next.Core
        IsPrerelease   = [bool] $Prerelease
        Bump           = $decided
        PreviousTag    = if ($null -eq $latest) { $null } else { $latest.Tag }
        Reason         = $reason
    }
}

function Test-IsRealRelease {
    <#
    .SYNOPSIS
      True only for a manual run on master with dry_run off, or a push to master when the repository variable
      RELEASE_ON_PUSH is exactly 'true'. Everything else (a push to master without that opt-in, hardening/**
      pushes, manual runs on other branches, pull requests, unknown events) is a dry run.
    .DESCRIPTION
      Merging into master therefore never publishes by surprise: by default the maintainer publishes with a
      manual run (docs/RELEASING.md, "How to turn on fully automatic releases").
    #>
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyString()] [string] $EventName,
        [AllowNull()] [AllowEmptyString()] [string] $Ref,
        [AllowNull()] [AllowEmptyString()] [string] $DryRunInput,
        [AllowNull()] [AllowEmptyString()] [string] $ReleaseOnPush
    )
    if ($Ref -cne 'refs/heads/master') { return $false }
    if ($EventName -ceq 'push') { return ($ReleaseOnPush -ceq 'true') }
    if ($EventName -ceq 'workflow_dispatch') { return ($DryRunInput -ceq 'false') }
    return $false
}

function Get-CodeSigningPlan {
    <#
    .SYNOPSIS
      Decides whether this run code-signs, and with which provider. Pure: the caller passes what is configured.
    .PARAMETER Provider
      The repository variable CODE_SIGNING_PROVIDER: '' (not configured), 'signpath' or 'azure'.
    .PARAMETER Configured
      The names of the repository variables/secrets that are set (non-empty). Secret VALUES are never passed.
    .PARAMETER IsRealRelease
      Only real releases sign. Dry runs (branches, pull requests, master without opt-in) never do.
    .OUTPUTS
      Provider ('none', 'signpath' or 'azure'), Missing (names still needed) and Note (one line for the summary).
      Throws when a real release has a provider selected but not all of its settings: shipping unsigned files by
      accident after the maintainer asked for signing would be worse than a failed run.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyString()] [string] $Provider,
        [AllowNull()] [AllowEmptyCollection()] [string[]] $Configured = @(),
        [switch] $IsRealRelease,
        [AllowNull()] [AllowEmptyString()] [string] $EventName
    )
    $requirements = @{
        signpath = @('SIGNPATH_ORGANIZATION_ID', 'SIGNPATH_PROJECT_SLUG', 'SIGNPATH_SIGNING_POLICY_SLUG',
            'SIGNPATH_APP_ARTIFACT_CONFIGURATION_SLUG', 'SIGNPATH_INSTALLER_ARTIFACT_CONFIGURATION_SLUG', 'SIGNPATH_API_TOKEN')
        azure    = @('AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_SUBSCRIPTION_ID', 'AZURE_SIGNING_ENDPOINT',
            'AZURE_SIGNING_ACCOUNT_NAME', 'AZURE_CERTIFICATE_PROFILE_NAME')
    }
    $name = if ($null -eq $Provider) { '' } else { $Provider.Trim().ToLowerInvariant() }
    if ($name -eq '' -or $name -eq 'none') {
        return [pscustomobject]@{ Provider = 'none'; Missing = @(); Note = 'Not code-signed: no signing provider is configured (CODE_SIGNING_PROVIDER).' }
    }
    if (-not $requirements.ContainsKey($name)) {
        throw "CODE_SIGNING_PROVIDER is '$Provider'; expected 'signpath', 'azure' or empty."
    }
    $present = @($Configured | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $missing = @($requirements[$name] | Where-Object { $_ -cnotin $present })
    if ($EventName -ceq 'pull_request' -or $EventName -ceq 'pull_request_target' -or -not $IsRealRelease) {
        $suffix = if ($missing.Count -gt 0) { " Still missing for a real release: $($missing -join ', ')." } else { '' }
        return [pscustomobject]@{ Provider = 'none'; Missing = $missing; Note = "Not code-signed: this is a dry run (a real release would sign with $name).$suffix" }
    }
    if ($missing.Count -gt 0) {
        throw "Code signing with '$name' is selected but these repository variables/secrets are not set: $($missing -join ', ')."
    }
    return [pscustomobject]@{ Provider = $name; Missing = @(); Note = "Code-signed with $name." }
}

function Get-ReleaseAssetName {
    <#
    .SYNOPSIS
      File names of the release assets: WinXCornersPlus-<version>-win-<arch>.zip and WinXCornersPlus-<version>-<arch>.msi.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [ValidateSet('zip', 'msi')] [string] $Kind,
        [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Architecture,
        [Parameter(Mandatory)] [string] $Version
    )
    if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(-rc\.[0-9]+)?\z') { throw "Version '$Version' is not a release version." }
    if ($Kind -ceq 'zip') { return "WinXCornersPlus-$Version-win-$Architecture.zip" }
    return "WinXCornersPlus-$Version-$Architecture.msi"
}

function ConvertTo-MsiProductVersion {
    <#
    .SYNOPSIS
      The MSI ProductVersion for a release: MAJOR.MINOR.PATCH without any prerelease suffix (Windows Installer
      versions are numeric, major and minor at most 255, the third field at most 65535).
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Version)
    $m = [regex]::Match($Version, '\A(?<major>0|[1-9][0-9]{0,4})\.(?<minor>0|[1-9][0-9]{0,4})\.(?<patch>0|[1-9][0-9]{0,4})(?:-rc\.[0-9]+)?\z')
    if (-not $m.Success) { throw "Version '$Version' is not a release version." }
    $major = [int] $m.Groups['major'].Value; $minor = [int] $m.Groups['minor'].Value; $patch = [int] $m.Groups['patch'].Value
    if ($major -gt 255 -or $minor -gt 255 -or $patch -gt 65535) {
        throw "Version '$Version' cannot be an MSI ProductVersion (major and minor at most 255, patch at most 65535)."
    }
    return '{0}.{1}.{2}' -f $major, $minor, $patch
}

function Format-Sha256Sums {
    <#
    .SYNOPSIS
      sha256sum-format text ("<hash>  <name>", LF line endings, sorted by name) for the given Name/Hash pairs.
    #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [object[]] $Entries)
    $lines = foreach ($entry in ($Entries | Sort-Object { $_.Name } -Culture ([System.Globalization.CultureInfo]::InvariantCulture))) {
        if ($entry.Hash -notmatch '\A[0-9a-fA-F]{64}\z') { throw "Hash for '$($entry.Name)' is not 64 hex characters." }
        if ($entry.Name -notmatch '\A[0-9A-Za-z._-]+\z' -or $entry.Name -in '.', '..') { throw "'$($entry.Name)' is not a plain file name." }
        '{0}  {1}' -f $entry.Hash.ToLowerInvariant(), $entry.Name
    }
    return (@($lines) -join "`n") + "`n"
}

function Select-ReleasePayload {
    <#
    .SYNOPSIS
      Filters publish-output paths (relative, either separator) down to what a user needs:
      drops debug symbols (*.pdb) and any settings.json, which would ship someone's configuration.
    #>
    [CmdletBinding()]
    param([AllowNull()] [AllowEmptyCollection()] [string[]] $RelativePaths)

    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($path in @($RelativePaths)) {
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $leaf = ($path -split '[\\/]')[-1]
        if ($leaf -like '*.pdb') { continue }
        if ($leaf -ieq 'settings.json') { continue }
        $kept.Add($path)
    }
    return $kept.ToArray()
}

function Test-VersionStamp {
    <#
    .SYNOPSIS
      Returns a list of problems (empty when fine) comparing a file's version resources with the
      computed release version. ProductVersion may carry "+<commit sha>" (added by the SDK's source
      revision support); that metadata is accepted and is what the app strips for display.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $FileName,
        [Parameter(Mandatory)] [string] $ExpectedVersion,
        [Parameter(Mandatory)] [string] $ExpectedNumericVersion,
        [AllowNull()] [AllowEmptyString()] [string] $FileVersion,
        [AllowNull()] [AllowEmptyString()] [string] $ProductVersion
    )
    $problems = [System.Collections.Generic.List[string]]::new()
    if ($FileVersion -cne $ExpectedNumericVersion) {
        $problems.Add("$FileName FileVersion is '$FileVersion', expected '$ExpectedNumericVersion'.")
    }
    $product = if ($null -eq $ProductVersion) { '' } else { $ProductVersion }
    $plus = $product.IndexOf('+')
    $productCore = if ($plus -ge 0) { $product.Substring(0, $plus) } else { $product }
    $metadataOk = $plus -lt 0 -or [regex]::IsMatch($product.Substring($plus + 1), '\A[0-9A-Za-z.-]+\z')
    if ($productCore -cne $ExpectedVersion -or -not $metadataOk) {
        $problems.Add("$FileName ProductVersion is '$ProductVersion', expected '$ExpectedVersion' (optionally followed by +metadata).")
    }
    return $problems.ToArray()
}

function ConvertFrom-Sha256Sums {
    <#
    .SYNOPSIS
      Parses sha256sum-style lines ("<64 hex>  <name>" or "<64 hex> *<name>") into Hash/Name pairs.
      Throws on any malformed line or on a name containing a path, so a tampered list cannot point
      the verifier at another file.
    #>
    [CmdletBinding()]
    param([AllowNull()] [AllowEmptyString()] [string] $Content)

    $entries = [System.Collections.Generic.List[object]]::new()
    $lineNumber = 0
    foreach ($line in ($(if ($null -eq $Content) { '' } else { $Content }) -split "`r?`n")) {
        $lineNumber++
        if ($line.Length -eq 0) { continue }
        $m = [regex]::Match($line, '\A(?<hash>[0-9a-fA-F]{64}) [ *](?<name>[^\\/:*?"<>|]+)\z')
        if (-not $m.Success -or $m.Groups['name'].Value -in '.', '..') {
            throw "SHA256SUMS line $lineNumber is malformed: '$line'."
        }
        $entries.Add([pscustomobject]@{ Hash = $m.Groups['hash'].Value.ToLowerInvariant(); Name = $m.Groups['name'].Value })
    }
    if ($entries.Count -eq 0) { throw 'SHA256SUMS is empty.' }
    return $entries.ToArray()
}

function Format-ReleaseNotes {
    <#
    .SYNOPSIS
      Builds the hand-written part of the GitHub Release body (GitHub's generated change list is appended
      after it by `gh release create --generate-notes`): which file is for whom, requirements, how to verify,
      and whether the files are code-signed.
    .PARAMETER Assets
      Name/Hash pairs for every release file (zips and MSIs), as read back from SHA256SUMS.txt.
    .PARAMETER Repository
      owner/name of the repository that builds the release (github.repository): used for links and for
      `gh attestation verify --repo`, so the notes are right in whichever repository runs the workflow.
    .PARAMETER SignedBy
      Empty when the files are not code-signed; otherwise the provider that signed them (signpath or azure).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [object[]] $Assets,
        [Parameter(Mandatory)] [string] $Repository,
        [string] $ServerUrl = 'https://github.com',
        [AllowNull()] [AllowEmptyString()] [string] $SignedBy,
        [switch] $Prerelease
    )
    if ($Repository -notmatch '\A[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z') { throw "Repository '$Repository' is not owner/name." }
    if ($ServerUrl -notmatch '\Ahttps://[A-Za-z0-9.-]+(:[0-9]+)?\z') { throw "Server URL '$ServerUrl' is not an https origin." }
    if ($SignedBy -and $SignedBy -cnotin 'signpath', 'azure') { throw "SignedBy '$SignedBy' must be signpath, azure or empty." }
    $byName = @{}
    foreach ($asset in $Assets) {
        if ($asset.Name -notmatch '\A[0-9A-Za-z._-]+\.(zip|msi)\z') { throw "Asset name '$($asset.Name)' is not a plain .zip or .msi file name." }
        if ($asset.Hash -notmatch '\A[0-9a-fA-F]{64}\z') { throw "Hash for '$($asset.Name)' must be 64 hex characters." }
        $byName[$asset.Name] = $asset.Hash.ToLowerInvariant()
    }
    $expected = foreach ($kind in 'msi', 'zip') { foreach ($arch in 'x64', 'arm64') { Get-ReleaseAssetName -Kind $kind -Architecture $arch -Version $Version } }
    foreach ($name in $expected) { if (-not $byName.ContainsKey($name)) { throw "Release asset $name is missing." } }
    $msiX64, $msiArm, $zipX64, $zipArm = $expected
    $project = "$ServerUrl/$Repository"

    $lines = [System.Collections.Generic.List[string]]::new()
    if ($Prerelease) {
        $lines.Add("> **Prerelease.** This is a release candidate for testing; it may change before the final $(($Version -split '-')[0]).")
        $lines.Add('')
    }
    $lines.Add('## Which file do I need?')
    $lines.Add('')
    $lines.Add('| File | For |')
    $lines.Add('|---|---|')
    $lines.Add("| ``$msiX64`` | **Recommended.** Installer for most PCs (Intel or AMD, 64-bit). |")
    $lines.Add("| ``$msiArm`` | Installer for Windows on Arm PCs (for example Snapdragon). |")
    $lines.Add("| ``$zipX64`` | Portable: extract anywhere and run ``WinXCornersPlus.exe`` (Intel or AMD). |")
    $lines.Add("| ``$zipArm`` | Portable, for Windows on Arm. |")
    $lines.Add('')
    $lines.Add('**Use the installer if you will ever run WinXCornersPlus as administrator.** It puts the program in `C:\Program Files\WinXCornersPlus`, which only administrators can change. A portable copy in Downloads or on the Desktop can be replaced by any program you run, and approving the administrator prompt would then run that replacement with full rights. The app shows this on its Security page.')
    $lines.Add('')
    $lines.Add('Not sure which PC you have? *Settings > System > About > System type* says "x64-based processor" or "ARM-based processor". There is no 32-bit (x86) build.')
    $lines.Add('')
    $lines.Add("**Requirements:** Windows 10 or 11 and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) for the same architecture (x64 or Arm64). The installer checks for it and tells you if it is missing; the portable app shows Windows' own prompt to download it.")
    $lines.Add('')
    $lines.Add('Installing, upgrading and what is kept on uninstall: [docs/INSTALLING.md](' + $project + '/blob/master/docs/INSTALLING.md).')
    $lines.Add('')
    $lines.Add('## Verify your download')
    $lines.Add('')
    $lines.Add('| File | SHA-256 |')
    $lines.Add('|---|---|')
    foreach ($name in $expected) { $lines.Add("| ``$name`` | ``$($byName[$name])`` |") }
    $lines.Add('')
    $lines.Add('In PowerShell, in the folder you downloaded to, compare the output with the table (or with the attached `SHA256SUMS.txt`):')
    $lines.Add('')
    $lines.Add('```powershell')
    $lines.Add("(Get-FileHash .\$msiX64 -Algorithm SHA256).Hash.ToLower()")
    $lines.Add('```')
    $lines.Add('')
    $lines.Add('To check that a file was built from this repository''s source by its release workflow (build provenance), use the [GitHub CLI](https://cli.github.com/):')
    $lines.Add('')
    $lines.Add('```')
    $lines.Add("gh attestation verify $msiX64 --repo $Repository")
    $lines.Add('```')
    $lines.Add('')
    if ($SignedBy -ceq 'signpath') {
        $lines.Add('## Code signing')
        $lines.Add('')
        $lines.Add('The program files and installers are Authenticode-signed. Free code signing provided by SignPath.io, certificate by SignPath Foundation (shown as the publisher).')
    }
    elseif ($SignedBy -ceq 'azure') {
        $lines.Add('## Code signing')
        $lines.Add('')
        $lines.Add('The program files and installers are Authenticode-signed through Azure Artifact Signing; Windows shows the maintainer as the publisher.')
    }
    else {
        $lines.Add('## Not code-signed')
        $lines.Add('')
        $lines.Add('The files are **not code-signed**. Windows SmartScreen and UAC will show **"Unknown publisher"** (and possibly "Windows protected your PC"; choose *More info* > *Run anyway*). Verify the hash or the attestation above before you trust a file.')
    }
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function ConvertFrom-ReleaseTag, ConvertFrom-BaseVersion, Get-LatestStableRelease, Get-CommitBump,
    Get-NextReleaseVersion, Test-IsRealRelease, Get-CodeSigningPlan, Get-ReleaseAssetName, ConvertTo-MsiProductVersion,
    Format-Sha256Sums, Select-ReleasePayload, Test-VersionStamp, ConvertFrom-Sha256Sums, Format-ReleaseNotes
