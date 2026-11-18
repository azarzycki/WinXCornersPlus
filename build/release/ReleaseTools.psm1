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
      True only for a push to master, or a manual run on master with dry_run off. Everything else
      (hardening/** pushes, manual runs on other branches, unknown events) is a dry run.
    #>
    [CmdletBinding()]
    param(
        [AllowNull()] [AllowEmptyString()] [string] $EventName,
        [AllowNull()] [AllowEmptyString()] [string] $Ref,
        [AllowNull()] [AllowEmptyString()] [string] $DryRunInput
    )
    if ($Ref -cne 'refs/heads/master') { return $false }
    if ($EventName -ceq 'push') { return $true }
    if ($EventName -ceq 'workflow_dispatch') { return ($DryRunInput -ceq 'false') }
    return $false
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
      Builds the hand-written part of the GitHub Release body (GitHub's generated change list is
      appended after it by `gh release create --generate-notes`).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $ZipName,
        [Parameter(Mandatory)] [string] $Sha256,
        [Parameter(Mandatory)] [string] $Repository,
        [switch] $Prerelease
    )
    if ($Repository -notmatch '\A[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z') { throw "Repository '$Repository' is not owner/name." }
    if ($Sha256 -notmatch '\A[0-9a-fA-F]{64}\z') { throw 'Sha256 must be 64 hex characters.' }
    if ($ZipName -notmatch '\A[0-9A-Za-z._-]+\.zip\z') { throw "Zip name '$ZipName' is not a plain file name." }
    $hash = $Sha256.ToLowerInvariant()

    $lines = [System.Collections.Generic.List[string]]::new()
    if ($Prerelease) {
        $lines.Add("> **Prerelease.** This is a release candidate for testing; it may change before the final $(($Version -split '-')[0]).")
        $lines.Add('')
    }
    $lines.Add("## Download")
    $lines.Add('')
    $lines.Add("Download ``$ZipName``, extract it anywhere you like and run ``WinXCornersPlus.exe``.")
    $lines.Add('')
    $lines.Add('**Requirements:** Windows 10 or 11 (x64) and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64). If it is missing, Windows offers to download it the first time you start the app.')
    $lines.Add('')
    $lines.Add('## Verify your download')
    $lines.Add('')
    $lines.Add("SHA-256 of ``$ZipName``:")
    $lines.Add('')
    $lines.Add('```')
    $lines.Add($hash)
    $lines.Add('```')
    $lines.Add('')
    $lines.Add('In PowerShell, in the folder you downloaded it to (the two lines must match):')
    $lines.Add('')
    $lines.Add('```powershell')
    $lines.Add("(Get-FileHash .\$ZipName -Algorithm SHA256).Hash.ToLower()")
    $lines.Add("'$hash'")
    $lines.Add('```')
    $lines.Add('')
    $lines.Add('The same hash is in the attached `SHA256SUMS.txt`. To check that the zip was built by this repository''s release workflow from its source (build provenance), use the [GitHub CLI](https://cli.github.com/):')
    $lines.Add('')
    $lines.Add('```')
    $lines.Add("gh attestation verify $ZipName --repo $Repository")
    $lines.Add('```')
    $lines.Add('')
    $lines.Add('## Not code-signed')
    $lines.Add('')
    $lines.Add('The executable is **not code-signed**. Windows SmartScreen and UAC will show **"Unknown publisher"** (and possibly "Windows protected your PC"; choose *More info* > *Run anyway*). Verify the hash or the attestation above before you trust the file.')
    return ($lines -join "`n") + "`n"
}

Export-ModuleMember -Function ConvertFrom-ReleaseTag, ConvertFrom-BaseVersion, Get-LatestStableRelease, Get-CommitBump,
    Get-NextReleaseVersion, Test-IsRealRelease, Select-ReleasePayload, Test-VersionStamp, ConvertFrom-Sha256Sums,
    Format-ReleaseNotes
