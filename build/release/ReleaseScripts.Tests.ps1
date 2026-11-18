#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# Integration tests for the thin scripts around ReleaseTools.psm1, using a throw-away git repository
# and temp files. Needs git on PATH.

BeforeAll {
    $script:scripts = $PSScriptRoot

    function New-TempDirectory {
        $path = Join-Path ([System.IO.Path]::GetTempPath()) ("wxc-release-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $path | Out-Null
        return $path
    }

    function Invoke-TestGit {
        param([string] $Repo, [string[]] $Arguments)
        & git -C $Repo -c user.name=test -c user.email=test@example.invalid -c commit.gpgsign=false -c tag.gpgsign=false @Arguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed" }
    }

    function New-TestRepository {
        param([string] $Version = '1.5.0')
        $repo = New-TempDirectory
        Invoke-TestGit $repo @('init', '-q', '-b', 'master')
        New-Item -ItemType Directory -Path (Join-Path $repo 'src') | Out-Null
        Set-Content -LiteralPath (Join-Path $repo 'src/App.csproj') -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>$Version</Version>
    <AssemblyVersion>$Version.0</AssemblyVersion>
  </PropertyGroup>
</Project>
"@
        Invoke-TestGit $repo @('add', '-A')
        Invoke-TestGit $repo @('commit', '-q', '-m', 'Initial commit')
        return $repo
    }

    function Add-TestCommit {
        param([string] $Repo, [string] $Message)
        Invoke-TestGit $Repo @('commit', '-q', '--allow-empty', '-m', $Message)
    }

    function Invoke-GetReleaseVersion {
        param([string] $Repo, [hashtable] $Extra = @{})
        $out = Join-Path $Repo 'github_output.txt'
        $summary = Join-Path $Repo 'summary.md'
        Remove-Item -LiteralPath $out, $summary -ErrorAction SilentlyContinue
        & (Join-Path $scripts 'Get-ReleaseVersion.ps1') -ProjectFile (Join-Path $Repo 'src/App.csproj') -RepositoryRoot $Repo `
            -OutputFile $out -SummaryFile $summary @Extra 6>&1 | Out-Null
        $values = @{}
        foreach ($line in Get-Content -LiteralPath $out) { $k, $v = $line -split '=', 2; $values[$k] = $v }
        $values['summary'] = Get-Content -LiteralPath $summary -Raw
        return $values
    }
}

Describe 'Get-ReleaseVersion.ps1' {
    AfterEach { if ($script:repo) { Remove-Item -LiteralPath $script:repo -Recurse -Force -ErrorAction SilentlyContinue } }

    It 'computes the base version for a repository without release tags' {
        $script:repo = New-TestRepository
        Invoke-TestGit $repo @('tag', 'vnext')
        $r = Invoke-GetReleaseVersion $repo
        $r.version | Should -Be '1.5.0'
        $r.tag | Should -Be 'v1.5.0'
        $r.numeric_version | Should -Be '1.5.0.0'
        $r.prerelease | Should -Be 'false'
        $r.bump | Should -Be 'initial'
        $r.previous_tag | Should -Be ''
        $r.summary | Should -BeLike '*Release version: ?1.5.0?*'
    }

    It 'only looks at commits after the latest stable tag' {
        $script:repo = New-TestRepository
        Add-TestCommit $repo 'feat: before the release'
        Invoke-TestGit $repo @('tag', 'v1.5.0')
        Add-TestCommit $repo 'Fix something'
        Add-TestCommit $repo "Multi-line message`n`nwith a body"
        $r = Invoke-GetReleaseVersion $repo
        $r.version | Should -Be '1.5.1'
        $r.previous_tag | Should -Be 'v1.5.0'
    }

    It 'sees a feat: commit and a breaking-change footer in the body' {
        $script:repo = New-TestRepository
        Invoke-TestGit $repo @('tag', 'v1.9.0')
        Invoke-TestGit $repo @('tag', 'v1.10.0')
        Add-TestCommit $repo 'feat(tray): new menu'
        (Invoke-GetReleaseVersion $repo).version | Should -Be '1.11.0'
        Add-TestCommit $repo "fix: settings`n`nBREAKING CHANGE: moved to APPDATA"
        (Invoke-GetReleaseVersion $repo).version | Should -Be '2.0.0'
    }

    It 'treats an empty bump as auto and passes a forced bump and prerelease through' {
        $script:repo = New-TestRepository
        Invoke-TestGit $repo @('tag', 'v1.5.0')
        (Invoke-GetReleaseVersion $repo @{ Bump = '' }).version | Should -Be '1.5.1'
        $r = Invoke-GetReleaseVersion $repo @{ Bump = 'minor'; Prerelease = $true; RunNumber = 17 }
        $r.version | Should -Be '1.6.0-rc.17'
        $r.prerelease | Should -Be 'true'
    }

    It 'rejects an unknown bump' {
        $script:repo = New-TestRepository
        { Invoke-GetReleaseVersion $repo @{ Bump = 'giant' } } | Should -Throw '*Bump must be*'
    }

    It 'refuses a shallow clone' {
        $script:repo = New-TestRepository
        Add-TestCommit $repo 'second'
        $clone = New-TempDirectory
        try {
            # file:///C:/... on Windows, file:///tmp/... elsewhere; --depth only applies to URL clones.
            $url = 'file:///' + ($repo -replace '\\', '/').TrimStart('/')
            & git clone -q --depth 1 $url $clone 2>$null
            $LASTEXITCODE | Should -Be 0
            { & (Join-Path $scripts 'Get-ReleaseVersion.ps1') -ProjectFile (Join-Path $clone 'src/App.csproj') -RepositoryRoot $clone -OutputFile '' -SummaryFile '' } |
                Should -Throw '*shallow*'
        }
        finally { Remove-Item -LiteralPath $clone -Recurse -Force -ErrorAction SilentlyContinue }
    }

    It 'refuses a project without exactly one <Version>' {
        $script:repo = New-TestRepository
        Set-Content -LiteralPath (Join-Path $repo 'src/App.csproj') -Value '<Project><PropertyGroup><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup></Project>'
        { Invoke-GetReleaseVersion $repo } | Should -Throw '*exactly one <Version>*'
    }
}

Describe 'Confirm-ReleaseChecksums.ps1 and New-ReleaseNotes.ps1' {
    BeforeEach {
        $script:dir = New-TempDirectory
        $script:zip = Join-Path $dir 'WinXCornersPlus-1.5.0-win-x64.zip'
        [System.IO.File]::WriteAllText($zip, 'not really a zip')
        $script:hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText((Join-Path $dir 'SHA256SUMS.txt'), "$hash  WinXCornersPlus-1.5.0-win-x64.zip`n")
    }
    AfterEach { Remove-Item -LiteralPath $script:dir -Recurse -Force -ErrorAction SilentlyContinue }

    It 'accepts matching checksums' {
        { & (Join-Path $scripts 'Confirm-ReleaseChecksums.ps1') -Directory $dir 6>$null } | Should -Not -Throw
    }

    It 'detects a modified file' {
        [System.IO.File]::AppendAllText($zip, 'tampered')
        { & (Join-Path $scripts 'Confirm-ReleaseChecksums.ps1') -Directory $dir 6>$null } | Should -Throw '*mismatch*'
    }

    It 'detects a missing file' {
        Remove-Item -LiteralPath $zip
        { & (Join-Path $scripts 'Confirm-ReleaseChecksums.ps1') -Directory $dir 6>$null } | Should -Throw '*missing*'
    }

    It 'writes release notes that carry the zip hash' {
        $notes = Join-Path $dir 'notes.md'
        & (Join-Path $scripts 'New-ReleaseNotes.ps1') -Directory $dir -Version '1.5.0' -Repository 'owner/repo' -OutputFile $notes 6>$null
        Get-Content -LiteralPath $notes -Raw | Should -BeLike "*$hash*gh attestation verify*"
    }

    It 'refuses to write notes for a version that is not in SHA256SUMS.txt' {
        { & (Join-Path $scripts 'New-ReleaseNotes.ps1') -Directory $dir -Version '1.5.1' -Repository 'o/r' -OutputFile (Join-Path $dir 'n.md') 6>$null } |
            Should -Throw '*no single entry*'
    }
}
