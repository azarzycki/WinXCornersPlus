#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# Unit tests for the pure release logic. Run: Invoke-Pester -Path build/release

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force
}

Describe 'ConvertFrom-ReleaseTag' {
    It 'parses <Tag>' -ForEach @(
        @{ Tag = 'v0.0.0'; Core = '0.0.0'; Pre = $null }
        @{ Tag = 'v1.5.0'; Core = '1.5.0'; Pre = $null }
        @{ Tag = 'v1.10.0'; Core = '1.10.0'; Pre = $null }
        @{ Tag = 'v65534.65534.65534'; Core = '65534.65534.65534'; Pre = $null }
        @{ Tag = 'v1.6.0-rc.12'; Core = '1.6.0'; Pre = 'rc.12' }
        @{ Tag = 'v2.0.0-beta-2.x'; Core = '2.0.0'; Pre = 'beta-2.x' }
    ) {
        $parsed = ConvertFrom-ReleaseTag $Tag
        $parsed.Core | Should -Be $Core
        $parsed.Prerelease | Should -Be $Pre
        $parsed.Tag | Should -Be $Tag
    }

    It 'ignores malformed tag <Tag>' -ForEach @(
        @{ Tag = 'v1' }, @{ Tag = 'v1.2' }, @{ Tag = 'vnext' }, @{ Tag = 'v01.2.3' }, @{ Tag = 'v1.02.3' },
        @{ Tag = 'v1.2.03' }, @{ Tag = '1.2.3' }, @{ Tag = 'V1.2.3' }, @{ Tag = 'v1.2.3.4' },
        @{ Tag = 'v1.2.3+build.5' }, @{ Tag = 'v1.2.3-' }, @{ Tag = 'v1.2.3-rc..1' }, @{ Tag = 'v1.2.3-rc_1' },
        @{ Tag = 'v-1.2.3' }, @{ Tag = ' v1.2.3' }, @{ Tag = "v1.2.3`n" }, @{ Tag = "v1.2.3`r" }, @{ Tag = 'v1.2.3 ' },
        @{ Tag = 'v65535.0.0' }, @{ Tag = 'v1.99999999999999999999.0' }, @{ Tag = 'v1.2.3;rm -rf /' },
        @{ Tag = "v1.$([char]0x0662).3" }, @{ Tag = "v1.2.$([char]0xFF13)" }, @{ Tag = 'refs/tags/v1.2.3' },
        @{ Tag = '' }, @{ Tag = $null }
    ) {
        ConvertFrom-ReleaseTag $Tag | Should -BeNullOrEmpty
    }

    It 'ignores an absurdly long tag without throwing' {
        ConvertFrom-ReleaseTag ('v1.2.3-' + ('a' * 100000)) | Should -BeNullOrEmpty
    }
}

Describe 'ConvertFrom-BaseVersion' {
    It 'accepts <Text>' -ForEach @(@{ Text = '1.5.0' }, @{ Text = ' 1.4.0 ' }, @{ Text = '0.0.1' }) {
        (ConvertFrom-BaseVersion $Text).Core | Should -Be $Text.Trim()
    }

    It 'rejects <Text> as a configuration error' -ForEach @(
        @{ Text = '' }, @{ Text = $null }, @{ Text = '1.5' }, @{ Text = '1.5.0.0' }, @{ Text = 'v1.5.0' },
        @{ Text = '1.5.0-rc.1' }, @{ Text = '01.5.0' }, @{ Text = '$(VersionPrefix)' }, @{ Text = '70000.0.0' }
    ) {
        { ConvertFrom-BaseVersion $Text } | Should -Throw '*base <Version>*'
    }
}

Describe 'Get-LatestStableRelease' {
    It 'returns nothing for no tags' {
        Get-LatestStableRelease @() | Should -BeNullOrEmpty
        Get-LatestStableRelease $null | Should -BeNullOrEmpty
    }

    It 'sorts numerically, not lexically' {
        (Get-LatestStableRelease @('v1.9.0', 'v1.10.0', 'v1.2.0')).Tag | Should -Be 'v1.10.0'
        (Get-LatestStableRelease @('v9.9.9', 'v10.0.0')).Tag | Should -Be 'v10.0.0'
        (Get-LatestStableRelease @('v1.2.10', 'v1.2.9')).Tag | Should -Be 'v1.2.10'
    }

    It 'ignores prerelease tags even when they are higher' {
        (Get-LatestStableRelease @('v1.5.0', 'v1.6.0-rc.3', 'v2.0.0-rc.1')).Tag | Should -Be 'v1.5.0'
    }

    It 'returns nothing when only prerelease and malformed tags exist' {
        Get-LatestStableRelease @('v1.6.0-rc.1', 'vnext', 'v1', 'latest', 'nightly') | Should -BeNullOrEmpty
    }

    It 'skips malformed tags that would otherwise sort highest' {
        (Get-LatestStableRelease @('v1.4.0', 'v99.0', 'v01.0.0', 'v100.0.0.0', 'v2.0.0+meta')).Tag | Should -Be 'v1.4.0'
    }

    It 'handles a huge tag list quickly' {
        $tags = [System.Collections.Generic.List[string]]::new()
        foreach ($i in 0..19999) { $tags.Add("v1.$i.0"); if ($i % 3 -eq 0) { $tags.Add("junk-$i") } }
        $elapsed = Measure-Command { $script:latest = Get-LatestStableRelease $tags.ToArray() }
        $script:latest.Tag | Should -Be 'v1.19999.0'
        $elapsed.TotalSeconds | Should -BeLessThan 30
    }
}

Describe 'Get-CommitBump' {
    It 'is patch for empty or missing history' {
        Get-CommitBump @() | Should -Be 'patch'
        Get-CommitBump $null | Should -Be 'patch'
        Get-CommitBump @('', '   ', $null) | Should -Be 'patch'
    }

    It 'is patch for ordinary, non-conventional messages' {
        Get-CommitBump @('Update README.md', 'Fix crash when settings are null', 'Merge pull request #4 from x/feat-thing') |
            Should -Be 'patch'
    }

    It 'is patch for fix:, chore:, docs: and look-alikes of feat' {
        Get-CommitBump @('fix: x', 'chore(deps): y', 'docs: z', 'feature: not feat', 'feat x', 'feats: no', 'refeat: no', 'feat(: bad') |
            Should -Be 'patch'
    }

    It 'is minor for <Subject>' -ForEach @(
        @{ Subject = 'feat: add a thing' }, @{ Subject = 'feat(tray): add a thing' }, @{ Subject = 'Feat: shouting' },
        @{ Subject = "  feat: leading whitespace" }, @{ Subject = "feat:`n`nbody only" }, @{ Subject = 'feat(): empty scope' }
    ) {
        Get-CommitBump @('fix: other', $Subject) | Should -Be 'minor'
    }

    It 'is major for <Message>' -ForEach @(
        @{ Message = 'feat!: drop Windows 10' }, @{ Message = 'refactor(settings)!: new format' }, @{ Message = 'fix!: x' },
        @{ Message = "fix: x`n`nBREAKING CHANGE: settings moved" }, @{ Message = "chore: y`n`nBREAKING-CHANGE: z" },
        @{ Message = "fix: x`r`n`r`nBREAKING CHANGE: windows line endings" }
    ) {
        Get-CommitBump @('feat: something', $Message, 'docs: z') | Should -Be 'major'
    }

    It 'does not treat <Message> as breaking' -ForEach @(
        @{ Message = "fix: x`n`nbreaking change: lowercase footer" },
        @{ Message = 'fix: mention BREAKING CHANGE: mid-line' },
        @{ Message = 'fix!:no space after the colon' },
        @{ Message = 'feat !: space before bang' }
    ) {
        Get-CommitBump @($Message) | Should -Not -Be 'major'
    }

    It 'handles a huge history quickly' {
        $messages = [string[]]@(foreach ($i in 1..20000) { "Commit number $i`n`nSome body text." }) + 'feat: last one'
        $elapsed = Measure-Command { $script:bump = Get-CommitBump $messages }
        $script:bump | Should -Be 'minor'
        $elapsed.TotalSeconds | Should -BeLessThan 30
    }
}

Describe 'Get-NextReleaseVersion' {
    It 'uses the base version for the first release' {
        $r = Get-NextReleaseVersion -Tags @() -CommitMessages @('feat!: x') -BaseVersion '1.5.0'
        $r.Version | Should -Be '1.5.0'
        $r.Tag | Should -Be 'v1.5.0'
        $r.NumericVersion | Should -Be '1.5.0.0'
        $r.Bump | Should -Be 'initial'
        $r.PreviousTag | Should -BeNullOrEmpty
        $r.IsPrerelease | Should -BeFalse
    }

    It 'treats a repository with only junk and prerelease tags as having no release' {
        $r = Get-NextReleaseVersion -Tags @('vnext', 'v1', 'v1.5.0-rc.4', 'archive/old') -BaseVersion '1.5.0'
        $r.Version | Should -Be '1.5.0'
        $r.Bump | Should -Be 'initial'
    }

    It 'ignores a forced bump on the first release and says so' {
        $r = Get-NextReleaseVersion -Tags @() -BaseVersion '1.5.0' -Bump major
        $r.Version | Should -Be '1.5.0'
        $r.Reason | Should -BeLike "*'major' bump does not apply*"
    }

    It 'bumps patch by default (non-conventional history)' {
        $r = Get-NextReleaseVersion -Tags @('v1.5.0') -CommitMessages @('Fix tray icon', 'Update README') -BaseVersion '1.5.0'
        $r.Version | Should -Be '1.5.1'
        $r.Bump | Should -Be 'patch'
        $r.PreviousTag | Should -Be 'v1.5.0'
    }

    It 'bumps patch even with no commits since the last tag' {
        (Get-NextReleaseVersion -Tags @('v1.5.0') -CommitMessages @() -BaseVersion '1.5.0').Version | Should -Be '1.5.1'
    }

    It 'bumps minor for a feat: commit and resets patch' {
        $r = Get-NextReleaseVersion -Tags @('v1.5.3') -CommitMessages @('fix: a', 'feat(tray): b') -BaseVersion '1.5.0'
        $r.Version | Should -Be '1.6.0'
        $r.Bump | Should -Be 'minor'
    }

    It 'bumps major for a breaking change and resets minor and patch' {
        $r = Get-NextReleaseVersion -Tags @('v1.5.3') -CommitMessages @('feat: a', "fix: b`n`nBREAKING CHANGE: c") -BaseVersion '1.5.0'
        $r.Version | Should -Be '2.0.0'
        $r.Bump | Should -Be 'major'
    }

    It 'bumps from the numerically latest stable tag' {
        (Get-NextReleaseVersion -Tags @('v1.9.0', 'v1.10.0', 'v1.11.0-rc.2', 'vnext') -BaseVersion '1.5.0').Version | Should -Be '1.10.1'
    }

    It 'honours a forced <Bump> bump over the commit messages' -ForEach @(
        @{ Bump = 'patch'; Expected = '1.5.1' }, @{ Bump = 'minor'; Expected = '1.6.0' }, @{ Bump = 'major'; Expected = '2.0.0' }
    ) {
        $r = Get-NextReleaseVersion -Tags @('v1.5.0') -CommitMessages @('feat!: would be major') -BaseVersion '1.0.0' -Bump $Bump
        $r.Version | Should -Be $Expected
        $r.Reason | Should -BeLike "*'$Bump' bump was requested*"
    }

    It 'uses the base version when a maintainer raised it above the computed one' {
        $r = Get-NextReleaseVersion -Tags @('v1.5.2') -CommitMessages @('fix: x') -BaseVersion '2.0.0'
        $r.Version | Should -Be '2.0.0'
        $r.Bump | Should -Be 'base'
        $r.Reason | Should -BeLike '*base version 2.0.0 is higher*'
    }

    It 'keeps the computed version when the base equals or trails it' {
        (Get-NextReleaseVersion -Tags @('v1.5.2') -BaseVersion '1.5.3').Version | Should -Be '1.5.3'
        (Get-NextReleaseVersion -Tags @('v1.5.2') -BaseVersion '1.5.3').Bump | Should -Be 'patch'
        (Get-NextReleaseVersion -Tags @('v1.5.2') -BaseVersion '1.4.0').Version | Should -Be '1.5.3'
    }

    It 'produces X.Y.Z-rc.<run> for a prerelease, based on the stable computation' {
        $r = Get-NextReleaseVersion -Tags @('v1.5.0', 'v1.6.0-rc.7') -CommitMessages @('feat: x') -BaseVersion '1.5.0' -Prerelease -RunNumber 42
        $r.Version | Should -Be '1.6.0-rc.42'
        $r.Tag | Should -Be 'v1.6.0-rc.42'
        $r.NumericVersion | Should -Be '1.6.0.0'
        $r.IsPrerelease | Should -BeTrue
    }

    It 'produces a first prerelease from the base version' {
        (Get-NextReleaseVersion -Tags @() -BaseVersion '1.5.0' -Prerelease -RunNumber 3).Version | Should -Be '1.5.0-rc.3'
    }

    It 'requires a run number for a prerelease' {
        { Get-NextReleaseVersion -Tags @() -BaseVersion '1.5.0' -Prerelease } | Should -Throw '*run number*'
    }

    It 'refuses a version whose tag already exists' {
        { Get-NextReleaseVersion -Tags @('v1.5.0', 'v1.5.1-rc.9') -BaseVersion '1.5.0' -Prerelease -RunNumber 9 } |
            Should -Throw '*already exists*'
    }

    It 'rejects an unknown bump' {
        { Get-NextReleaseVersion -Tags @() -BaseVersion '1.5.0' -Bump 'huge' } | Should -Throw
    }

    It 'rejects a broken base version even when tags exist' {
        { Get-NextReleaseVersion -Tags @('v1.5.0') -BaseVersion 'one' } | Should -Throw '*base <Version>*'
    }

    It 'fails cleanly instead of overflowing a version component' {
        { Get-NextReleaseVersion -Tags @('v1.65534.0') -CommitMessages @('feat: x') -BaseVersion '1.0.0' } |
            Should -Throw '*exceed 65534*'
    }

    It 'is not confused by hostile tag and commit text' {
        $tags = @('v1.5.0', 'v1.5.0$(whoami)', "v9.9.9`nversion=6.6.6", 'v1.6.0;echo pwned', '-rf', '--help', '')
        $messages = @("version=9.9.9`ntag=v9.9.9", '$(Remove-Item -Recurse /)', "`0`0", 'x' * 100000)
        $r = Get-NextReleaseVersion -Tags $tags -CommitMessages $messages -BaseVersion '1.5.0'
        $r.Version | Should -Be '1.5.1'
    }
}

Describe 'Test-IsRealRelease' {
    It 'is <Expected> for <EventName> on <Ref> (dry_run=<DryRun>)' -ForEach @(
        @{ EventName = 'push'; Ref = 'refs/heads/master'; DryRun = ''; Expected = $true }
        @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/master'; DryRun = 'false'; Expected = $true }
        @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/master'; DryRun = 'true'; Expected = $false }
        @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/master'; DryRun = ''; Expected = $false }
        @{ EventName = 'workflow_dispatch'; Ref = 'refs/heads/hardening/x'; DryRun = 'false'; Expected = $false }
        @{ EventName = 'push'; Ref = 'refs/heads/hardening/release-pipeline'; DryRun = ''; Expected = $false }
        @{ EventName = 'push'; Ref = 'refs/heads/master-old'; DryRun = ''; Expected = $false }
        @{ EventName = 'push'; Ref = 'refs/tags/v1.5.0'; DryRun = ''; Expected = $false }
        @{ EventName = 'pull_request'; Ref = 'refs/heads/master'; DryRun = ''; Expected = $false }
        @{ EventName = 'push'; Ref = 'refs/heads/Master'; DryRun = ''; Expected = $false }
        @{ EventName = ''; Ref = ''; DryRun = ''; Expected = $false }
    ) {
        Test-IsRealRelease -EventName $EventName -Ref $Ref -DryRunInput $DryRun | Should -Be $Expected
    }
}

Describe 'Select-ReleasePayload' {
    It 'keeps the app and drops symbols and settings files' {
        $kept = Select-ReleasePayload @(
            'WinXCornersPlus.exe', 'WinXCornersPlus.dll', 'WinXCornersPlus.pdb', 'WinXCorners.Core.dll', 'WinXCorners.Core.pdb',
            'WinXCornersPlus.deps.json', 'WinXCornersPlus.runtimeconfig.json', 'settings.json', 'sub/Settings.JSON', 'de\x.resources.dll',
            'sub\debug.PDB', '', $null)
        $kept | Should -Be @('WinXCornersPlus.exe', 'WinXCornersPlus.dll', 'WinXCorners.Core.dll', 'WinXCornersPlus.deps.json',
            'WinXCornersPlus.runtimeconfig.json', 'de\x.resources.dll')
    }

    It 'returns an empty array for no input' {
        @(Select-ReleasePayload @()).Count | Should -Be 0
    }
}

Describe 'Test-VersionStamp' {
    It 'accepts <Product>' -ForEach @(
        @{ Version = '1.5.0'; Product = '1.5.0' }
        @{ Version = '1.5.0'; Product = '1.5.0+0e4f9e4a1b2c3d' }
        @{ Version = '1.6.0-rc.7'; Product = '1.6.0-rc.7+abc' }
    ) {
        @(Test-VersionStamp -FileName 'a.exe' -ExpectedVersion $Version -ExpectedNumericVersion (($Version -split '-')[0] + '.0') `
            -FileVersion (($Version -split '-')[0] + '.0') -ProductVersion $Product).Count | Should -Be 0
    }

    It 'reports a wrong <What>' -ForEach @(
        @{ What = 'file version'; File = '1.4.0.0'; Product = '1.5.0' }
        @{ What = 'product version'; File = '1.5.0.0'; Product = '1.4.0' }
        @{ What = 'product prefix'; File = '1.5.0.0'; Product = '1.5.0.1' }
        @{ What = 'prerelease leak'; File = '1.5.0.0'; Product = '1.5.0-rc.1' }
        @{ What = 'bad metadata'; File = '1.5.0.0'; Product = '1.5.0+' }
        @{ What = 'missing resources'; File = $null; Product = $null }
    ) {
        @(Test-VersionStamp -FileName 'a.exe' -ExpectedVersion '1.5.0' -ExpectedNumericVersion '1.5.0.0' -FileVersion $File -ProductVersion $Product).Count |
            Should -BeGreaterThan 0
    }
}

Describe 'ConvertFrom-Sha256Sums' {
    BeforeAll { $script:h = 'ab' * 32 }

    It 'parses text- and binary-mode lines with either line ending' {
        $entries = ConvertFrom-Sha256Sums ("$($h.ToUpper())  a.zip`r`n$h *b.txt`n")
        $entries.Count | Should -Be 2
        $entries[0].Hash | Should -Be $h
        $entries[0].Name | Should -Be 'a.zip'
        $entries[1].Name | Should -Be 'b.txt'
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'empty'; Text = '' }
        @{ Name = 'short hash'; Text = 'abcd  a.zip' }
        @{ Name = 'path traversal'; Text = ('ab' * 32) + '  ../evil.zip' }
        @{ Name = 'absolute path'; Text = ('ab' * 32) + '  C:\evil.zip' }
        @{ Name = 'dot-dot'; Text = ('ab' * 32) + '  ..' }
        @{ Name = 'garbage line'; Text = ('ab' * 32) + "  a.zip`nhello" }
    ) {
        { ConvertFrom-Sha256Sums $Text } | Should -Throw
    }
}

Describe 'Format-ReleaseNotes' {
    BeforeAll {
        $script:hash = ('0123456789ABCDEF' * 4)
        $script:notes = Format-ReleaseNotes -Version '1.5.0' -ZipName 'WinXCornersPlus-1.5.0-win-x64.zip' -Sha256 $hash -Repository 'owner/WinXCornersPlus'
    }

    It 'states the lower-case SHA-256' { $notes | Should -BeLike "*$($hash.ToLower())*" }
    It 'explains Get-FileHash' { $notes | Should -BeLike '*Get-FileHash .\WinXCornersPlus-1.5.0-win-x64.zip -Algorithm SHA256*' }
    It 'explains gh attestation verify' { $notes | Should -BeLike '*gh attestation verify WinXCornersPlus-1.5.0-win-x64.zip --repo owner/WinXCornersPlus*' }
    It 'says it is not code-signed and shows Unknown publisher' {
        $notes | Should -BeLike '*not code-signed*'
        $notes | Should -BeLike '*Unknown publisher*'
    }
    It 'names the .NET 10 Desktop Runtime' { $notes | Should -BeLike '*.NET 10 Desktop Runtime*' }
    It 'has no prerelease banner for a stable release' { $notes | Should -Not -BeLike '*Prerelease.*' }

    It 'adds a prerelease banner for a prerelease' {
        Format-ReleaseNotes -Version '1.6.0-rc.4' -ZipName 'WinXCornersPlus-1.6.0-rc.4-win-x64.zip' -Sha256 $hash -Repository 'o/r' -Prerelease |
            Should -BeLike '*Prerelease.*final 1.6.0*'
    }

    It 'rejects <Name>' -ForEach @(
        @{ Name = 'bad repository'; Repo = 'o/r; rm -rf'; Zip = 'a.zip'; Sha = ('ab' * 32) }
        @{ Name = 'bad hash'; Repo = 'o/r'; Zip = 'a.zip'; Sha = 'xyz' }
        @{ Name = 'path in zip name'; Repo = 'o/r'; Zip = '../a.zip'; Sha = ('ab' * 32) }
    ) {
        { Format-ReleaseNotes -Version '1.5.0' -ZipName $Zip -Sha256 $Sha -Repository $Repo } | Should -Throw
    }
}
