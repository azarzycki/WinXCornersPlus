#Requires -Modules @{ ModuleName = 'Pester'; ModuleVersion = '5.0.0' }
# Static tests of .github/workflows/release.yml: what can never run when. The signing stage cannot be executed
# without credentials, so these checks are what keeps it gated. Needs the powershell-yaml module (0.4.x).

BeforeDiscovery {
    $script:signingJobs = @('sign-app', 'sign-installer')
}

BeforeAll {
    Import-Module powershell-yaml -ErrorAction Stop
    $script:path = Join-Path $PSScriptRoot '../../.github/workflows/release.yml'
    $script:text = Get-Content -LiteralPath $path -Raw
    $script:workflow = ConvertFrom-Yaml $text -Ordered
    $script:jobs = $workflow['jobs']
    # YAML 1.1 reads the key "on" as boolean true.
    $script:triggers = if ($workflow.Contains('on')) { $workflow['on'] } else { $workflow[$true] }

    function Get-Steps([string] $Job) { @($jobs[$Job]['steps']) }
    function Get-Uses($Step) { if ($Step.Contains('uses')) { [string] $Step['uses'] } else { '' } }
    function Get-If($Item) { if ($Item.Contains('if')) { ([string] $Item['if']) -replace '\s+', ' ' } else { '' } }
    function Get-Needs([string] $Job) { @($jobs[$Job]['needs']) }
}

Describe 'release.yml triggers' {
    It 'never runs for pull requests' {
        $triggers.Keys | Should -Not -Contain 'pull_request'
        $triggers.Keys | Should -Not -Contain 'pull_request_target'
        $triggers.Keys | Should -Be @('push', 'workflow_dispatch')
    }

    It 'defaults a manual run to a dry run' {
        $triggers['workflow_dispatch']['inputs']['dry_run']['default'] | Should -BeTrue
    }

    It 'grants no permissions at the top level' {
        $workflow['permissions'].Count | Should -Be 0
    }
}

Describe 'dry run or real release' {
    It 'is decided only by Test-IsRealRelease, with the RELEASE_ON_PUSH repository variable and the dry_run input' {
        $mode = Get-Steps 'version' | Where-Object { $_['id'] -eq 'mode' }
        $mode['env']['RELEASE_ON_PUSH'] | Should -Be '${{ vars.RELEASE_ON_PUSH }}'
        $mode['env']['INPUT_DRY_RUN'] | Should -Be '${{ inputs.dry_run }}'
        $mode['run'] | Should -BeLike '*Test-IsRealRelease*-ReleaseOnPush $env:RELEASE_ON_PUSH*'
        $jobs['version']['outputs']['dry_run'] | Should -Be '${{ steps.mode.outputs.dry_run }}'
    }

    It 'publishes only on a real release that passed the CI gate' {
        Get-Needs 'publish' | Should -Contain 'gate'
        Get-Needs 'publish' | Should -Contain 'version'
        $condition = Get-If $jobs['publish']
        $condition | Should -BeLike "*needs.version.outputs.dry_run == 'false'*"
        $condition | Should -BeLike "*needs.gate.result == 'success'*"
        Get-Needs 'publish' | Should -Contain 'gate-ui'
        $condition | Should -BeLike "*needs.gate-ui.result == 'success'*"
        $condition | Should -Not -BeLike '*always()*'
        $condition | Should -Not -BeLike '*||*'
    }

    It 'is the only job that can write to the repository or create attestations' {
        foreach ($name in $jobs.Keys) {
            if ($name -eq 'publish' -or -not $jobs[$name].Contains('permissions')) { continue }
            $permissions = $jobs[$name]['permissions']
            $permissions['contents'] | Should -Not -Be 'write' -Because "job $name"
            $permissions.Contains('attestations') | Should -BeFalse -Because "job $name"
        }
    }

    It 'gives OpenID Connect tokens only to the signing jobs and to publish' {
        foreach ($name in $jobs.Keys) {
            if (-not $jobs[$name].Contains('permissions')) { continue }
            if ($jobs[$name]['permissions'].Contains('id-token')) { $name | Should -BeIn @('sign-app', 'sign-installer', 'publish') }
        }
    }
}

Describe 'the code-signing stage' {
    It '<_> runs only on a real release with a configured provider, never for a pull request' -ForEach $signingJobs {
        $condition = Get-If $jobs[$_]
        $condition | Should -BeLike "*needs.version.outputs.signing_provider != 'none'*"
        $condition | Should -BeLike "*needs.version.outputs.dry_run == 'false'*"
        $condition | Should -BeLike "*github.event_name != 'pull_request'*"
        $condition | Should -Not -BeLike '*||*'
        Get-Needs $_ | Should -Contain 'version'
    }

    It '<_> gates every provider-specific step on its own provider' -ForEach $signingJobs {
        foreach ($step in (Get-Steps $_)) {
            $uses = Get-Uses $step
            $name = if ($step.Contains('name')) { $step['name'] } else { $uses }
            if ($uses -like 'signpath/*' -or $name -like 'SignPath*') { Get-If $step | Should -Be "env.PROVIDER == 'signpath'" -Because $name }
            if ($uses -like 'azure/*' -or $name -like 'Azure*') { Get-If $step | Should -Be "env.PROVIDER == 'azure'" -Because $name }
        }
        $jobs[$_]['env']['PROVIDER'] | Should -Be '${{ needs.version.outputs.signing_provider }}'
    }

    It '<_> verifies every signature before uploading' -ForEach $signingJobs {
        $steps = Get-Steps $_
        $verify = [array]::FindIndex([object[]] $steps, [Predicate[object]] { param($s) $s.Contains('run') -and $s['run'] -like '*Assert-AuthenticodeSigned*' })
        $upload = [array]::FindLastIndex([object[]] $steps, [Predicate[object]] { param($s) (Get-Uses $s) -like 'actions/upload-artifact@*' })
        $verify | Should -BeGreaterThan -1
        $verify | Should -BeLessThan $upload
        Get-If $steps[$verify] | Should -Be ''
    }

    It 'uses signing actions and secrets nowhere else' {
        foreach ($name in $jobs.Keys) {
            if ($name -in $signingJobs) { continue }
            foreach ($step in (Get-Steps $name)) {
                Get-Uses $step | Should -Not -BeLike 'signpath/*' -Because "job $name"
                Get-Uses $step | Should -Not -BeLike 'azure/*' -Because "job $name"
            }
        }
        # Outside the signing jobs a secret may only be tested for presence (in the version job), never read.
        foreach ($match in [regex]::Matches($text, '\$\{\{[^}]*secrets\.[^}]*\}\}')) {
            $line = $text.Substring(0, $match.Index).Split("`n").Count
            if ($match.Value -notmatch "\A\$\{\{ secrets\.[A-Z_]+ != '' \}\}\z") {
                $match.Value | Should -Match "secrets\.SIGNPATH_API_TOKEN" -Because "line $line"
                $match.Value | Should -Be '${{ secrets.SIGNPATH_API_TOKEN }}' -Because "line $line"
            }
        }
    }

    It 'decides the provider with Get-CodeSigningPlan from the dry-run decision' {
        $signing = Get-Steps 'version' | Where-Object { $_['id'] -eq 'signing' }
        $signing['run'] | Should -BeLike '*Get-CodeSigningPlan*-IsRealRelease:($env:DRY_RUN -eq ''false'')*-EventName $env:EVENT_NAME*'
        $signing['env']['DRY_RUN'] | Should -Be '${{ steps.mode.outputs.dry_run }}'
        $jobs['version']['outputs']['signing_provider'] | Should -Be '${{ steps.signing.outputs.provider }}'
    }

    It 'sits between build and packaging, and between packaging and the checksums' {
        Get-Needs 'sign-app' | Should -Contain 'build'
        Get-Needs 'package' | Should -Contain 'sign-app'
        Get-Needs 'sign-installer' | Should -Contain 'package'
        Get-Needs 'installer-tests' | Should -Contain 'sign-installer'
        Get-Needs 'notes' | Should -Contain 'sign-installer'
        Get-Needs 'notes' | Should -Contain 'installer-tests'
    }

    It 'lets packaging continue only if signing succeeded or was skipped because this run does not sign' {
        Get-If $jobs['package'] | Should -BeLike "*needs.sign-app.result == 'success' || (needs.sign-app.result == 'skipped' && needs.version.outputs.signing_provider == 'none')*"
        Get-If $jobs['installer-tests'] | Should -BeLike "*needs.sign-installer.result == 'success' || (needs.sign-installer.result == 'skipped' && needs.version.outputs.signing_provider == 'none')*"
    }
}

Describe 'actions' {
    It 'pins every third-party action to a full commit SHA' {
        foreach ($name in $jobs.Keys) {
            foreach ($step in (Get-Steps $name)) {
                $uses = Get-Uses $step
                if ($uses -and $uses -notlike './*') { $uses | Should -Match '@[0-9a-f]{40}\z' -Because "job $name" }
            }
        }
    }

    It 'never checks out with stored credentials' {
        foreach ($name in $jobs.Keys) {
            foreach ($step in (Get-Steps $name)) {
                if ((Get-Uses $step) -like 'actions/checkout@*') { $step['with']['persist-credentials'] | Should -BeFalse -Because "job $name" }
            }
        }
    }

    It 'builds and tests each architecture on a matching runner' {
        foreach ($name in 'build', 'package', 'installer-tests') {
            $include = @($jobs[$name]['strategy']['matrix']['include'])
            ($include | Where-Object { $_['arch'] -eq 'x64' })['runner'] | Should -Match '\Awindows-(20[0-9]{2}|latest)\z'
            ($include | Where-Object { $_['arch'] -eq 'arm64' })['runner'] | Should -Be 'windows-11-arm'
        }
    }

    It 'does not hard-code a repository owner' {
        $text | Should -Not -Match '(?i)github\.com/[A-Za-z0-9-]+/WinXCornersPlus'
    }
}
