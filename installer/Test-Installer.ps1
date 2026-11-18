<#
.SYNOPSIS
  End-to-end test of the WinXCornersPlus MSI on a disposable Windows machine (a GitHub Actions runner).
  Installs, inspects, upgrades, refuses a downgrade, uninstalls and checks the missing-runtime stop, with every
  msiexec run logged verbosely (/l*v) into -LogDirectory.

.DESCRIPTION
  DESTRUCTIVE: installs and removes software machine-wide, creates a local standard user, and writes and deletes
  HKLM\SOFTWARE\WinXCornersPlus and the current user's Run value WinXCornersPlus. Never run it on your own PC.

  Checks (docs/RELEASING.md lists them too):
    1 install       silent per-machine install succeeds; files, exe architecture/version, Start-menu shortcut and
                    one Add/Remove Programs entry with the right name, version, publisher and link
    2 acl           the install folder is not writable by non-administrators: no write-type Allow rule for
                    Everyone, Authenticated Users, Users or INTERACTIVE, an administrative owner, and the app's own
                    InstallLocationProbe.IsWritableByNonAdministrators returns false (with a positive control)
    3 run           the installed app starts as a standard user (it finds the .NET runtime), stays alive, runs
                    natively, and exits cleanly (exit code 0) when sent what the installer sends
    4 upgrade       a higher version installs over the first WHILE the app runs: exit 0 (not 3010), one Add/Remove
                    Programs entry with the new version, the old process is gone and was not restarted
    5 downgrade     installing the older package over the newer one is refused with the downgrade message
    6 uninstall     with the app running: files, shortcut, entry, HKLM\SOFTWARE\WinXCornersPlus and the HKCU Run
                    value are gone; %APPDATA%\WinXCornersPlus\settings.json is kept
    7 no runtime    a package whose prerequisite cannot be met stops with the prerequisite message, installs nothing
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Msi,
    [Parameter(Mandatory)] [string] $UpgradeMsi,
    [Parameter(Mandatory)] [string] $NoRuntimeMsi,
    [Parameter(Mandatory)] [string] $PayloadDirectory,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Architecture,
    [Parameter(Mandatory)] [string] $ProductVersion,
    [Parameter(Mandatory)] [string] $UpgradeProductVersion,
    [Parameter(Mandatory)] [string] $ProjectUrl,
    [Parameter(Mandatory)] [string] $LogDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot '../build/release/ReleaseFiles.psm1') -Force

$installDir = Join-Path $env:ProgramW6432 'WinXCornersPlus'
$exeName = 'WinXCornersPlus.exe'
$installedExe = Join-Path $installDir $exeName
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'WinXCornersPlus.lnk'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$downgradeText = 'A newer version of WinXCornersPlus is already installed'
$runtimeText = 'WinXCornersPlus needs the .NET 10 Desktop Runtime'
New-Item -ItemType Directory -Force -Path $LogDirectory | Out-Null
$LogDirectory = (Resolve-Path -LiteralPath $LogDirectory).Path
$Msi = (Resolve-Path -LiteralPath $Msi).Path
$UpgradeMsi = (Resolve-Path -LiteralPath $UpgradeMsi).Path
$NoRuntimeMsi = (Resolve-Path -LiteralPath $NoRuntimeMsi).Path

# --- Bookkeeping -------------------------------------------------------------------------------------------
$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string] $Step, [string] $Check, [bool] $Passed, [string] $Detail = '') {
    $results.Add([pscustomobject]@{ Step = $Step; Check = $Check; Result = $(if ($Passed) { 'pass' } else { 'FAIL' }); Detail = $Detail })
    $color = if ($Passed) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1}: {2} {3}" -f $(if ($Passed) { 'pass' } else { 'FAIL' }), $Step, $Check, $Detail) -ForegroundColor $color
}
function Add-Info([string] $Step, [string] $Check, [string] $Detail) {
    $results.Add([pscustomobject]@{ Step = $Step; Check = $Check; Result = 'info'; Detail = $Detail })
    Write-Host "[info] ${Step}: $Check $Detail"
}

function Invoke-Msiexec([string] $Name, [string[]] $Arguments) {
    $log = Join-Path $LogDirectory "$Name.log"
    $all = @($Arguments) + @('/qn', '/norestart', '/l*v', "`"$log`"")
    Write-Host "msiexec $($all -join ' ')"
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath (Join-Path ([Environment]::SystemDirectory) 'msiexec.exe') -ArgumentList $all -Wait -PassThru
    Write-Host "  exit code $($process.ExitCode) after $([int] $clock.Elapsed.TotalSeconds) s; log $log"
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Log = $log; Text = $(if (Test-Path -LiteralPath $log) { [System.IO.File]::ReadAllText($log) } else { '' }) }
}

function Get-ArpEntries {
    @(Get-ChildItem 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' | ForEach-Object {
            $item = Get-ItemProperty -LiteralPath $_.PSPath
            $name = $item.PSObject.Properties['DisplayName']
            if ($name -and $name.Value -eq 'WinXCornersPlus') { $item }
        })
}
function Get-Value($Object, [string] $Name) { $p = $Object.PSObject.Properties[$Name]; if ($p) { $p.Value } else { $null } }

function Get-AppProcesses { @(Get-Process -Name 'WinXCornersPlus' -ErrorAction SilentlyContinue) }

# --- A standard user to run the app as (an elevated copy would stop at its approval dialog) ------------------
$testUser = 'wxcinstalltest'
$credential = New-StandardTestUser -Name $testUser
$testStarted = Get-Date

function Start-AppAsStandardUser([string] $Step) {
    $process = Start-ProcessAsUser -Credential $credential -FilePath $installedExe
    if ($process.WaitForExit(8000)) {
        Add-Result $Step 'app starts as a standard user and stays alive' $false "exited with code $($process.ExitCode)"
        Write-AppCrashInformation -Since $testStarted
        return $null
    }
    $path = Get-ProcessImagePath $process.Id
    Add-Result $Step 'app starts as a standard user and stays alive' ($path -ieq $installedExe) "pid $($process.Id), $path"
    return $process
}

function Wait-Exit($Process, [int] $Seconds) {
    if ($Process.WaitForExit($Seconds * 1000)) { return $Process.ExitCode }
    return $null
}

function Test-WriteTypeAllowRules([string] $Path) {
    # Same rights and groups as the app's InstallLocationProbe, matched by SID.
    $writeMask = [int] ([System.Security.AccessControl.FileSystemRights]::WriteData -bor
        [System.Security.AccessControl.FileSystemRights]::AppendData -bor [System.Security.AccessControl.FileSystemRights]::Delete -bor
        [System.Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [System.Security.AccessControl.FileSystemRights]::ChangePermissions -bor [System.Security.AccessControl.FileSystemRights]::TakeOwnership) -bor
        0x10000000 -bor 0x40000000 # GENERIC_ALL, GENERIC_WRITE
    $nonAdmins = @('S-1-1-0', 'S-1-5-11', 'S-1-5-32-545', 'S-1-5-4')
    $acl = Get-Acl -LiteralPath $Path
    $rules = @($acl.GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier]))
    foreach ($rule in $rules) {
        Write-Host ("  {0} {1} {2} inherited={3} ({4})" -f $rule.AccessControlType, $rule.IdentityReference.Value, $rule.FileSystemRights,
            $rule.IsInherited, $(try { $rule.IdentityReference.Translate([System.Security.Principal.NTAccount]).Value } catch { '?' }))
    }
    $offending = @($rules | Where-Object {
            $_.AccessControlType -eq 'Allow' -and $_.IdentityReference.Value -in $nonAdmins -and (([int] $_.FileSystemRights) -band $writeMask) -ne 0 })
    $owner = $acl.GetOwner([System.Security.Principal.SecurityIdentifier]).Value
    return [pscustomobject]@{ Offending = $offending; Owner = $owner }
}

function Invoke-AppProbe([string] $Directory) {
    # Loads the INSTALLED app assembly and calls its own InstallLocationProbe (internal) through reflection.
    $work = Join-Path ([System.IO.Path]::GetTempPath()) 'wxc-probe'
    if (-not (Test-Path -LiteralPath (Join-Path $work 'bin'))) {
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        Set-Content -LiteralPath (Join-Path $work 'probe.csproj') -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
'@
        Set-Content -LiteralPath (Join-Path $work 'Program.cs') -Value @'
using System.Reflection;
var assembly = Assembly.LoadFrom(args[0]);
var probe = assembly.GetType("WinXCorners.App.InstallLocationProbe", throwOnError: true)!;
var method = probe.GetMethod("IsWritableByNonAdministrators", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
var result = (bool?)method.Invoke(null, new object[] { args[1] });
Console.WriteLine(result is null ? "null" : result.Value ? "true" : "false");
'@
        & dotnet build $work -c Release -o (Join-Path $work 'bin') -nologo -v q | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not build the probe helper.' }
    }
    $output = & (Join-Path $work 'bin/probe.exe') (Join-Path $installDir 'WinXCornersPlus.dll') $Directory
    if ($LASTEXITCODE -ne 0) { throw "The probe helper failed (exit code $LASTEXITCODE): $output" }
    return ($output | Select-Object -Last 1).Trim()
}

$createdSettings = $false
$settingsFile = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'WinXCornersPlus\settings.json'
try {
    if (@(Get-ArpEntries).Count -gt 0 -or (Test-Path -LiteralPath $installDir) -or @(Get-AppProcesses).Count -gt 0) {
        throw 'WinXCornersPlus is already installed or running on this runner; the test needs a clean machine.'
    }

    # ---- 1. Install ---------------------------------------------------------------------------------------
    $run = Invoke-Msiexec '1-install' @('/i', "`"$Msi`"")
    Add-Result '1 install' 'silent install exits with 0' ($run.ExitCode -eq 0) "exit code $($run.ExitCode)"
    $expectedFiles = @(Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath((Resolve-Path $PayloadDirectory).Path, $_.FullName) } | Sort-Object)
    $installedFiles = if (Test-Path -LiteralPath $installDir) { @(Get-ChildItem -LiteralPath $installDir -Recurse -File | ForEach-Object { [System.IO.Path]::GetRelativePath($installDir, $_.FullName) } | Sort-Object) } else { @() }
    Add-Result '1 install' "files in $installDir" (($installedFiles -join '|') -ceq ($expectedFiles -join '|')) ($installedFiles -join ', ')
    foreach ($required in 'WinXCornersPlus.exe', 'WinXCornersPlus.dll', 'WinXCorners.Core.dll', 'WinXCornersPlus.runtimeconfig.json', 'WinXCornersPlus.deps.json', 'LICENSE.txt') {
        Add-Result '1 install' "$required installed" ($required -in $installedFiles)
    }
    if (Test-Path -LiteralPath $installedExe) {
        $machine = Get-MachineName (Get-PeMachine $installedExe)
        Add-Result '1 install' 'installed exe is the package architecture' ($machine -eq $Architecture) $machine
        Add-Info '1 install' 'installed exe version' ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedExe).ProductVersion)
    }
    $lnk = if (Test-Path -LiteralPath $shortcut) { (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut).TargetPath } else { $null }
    Add-Result '1 install' 'Start-menu shortcut points to the installed exe' ($lnk -ieq $installedExe) "$shortcut -> $lnk"
    $arp = @(Get-ArpEntries)
    Add-Result '1 install' 'one Add/Remove Programs entry' ($arp.Count -eq 1) "$($arp.Count) entries"
    if ($arp.Count -ge 1) {
        $entry = $arp[0]
        Add-Result '1 install' 'entry version' ((Get-Value $entry 'DisplayVersion') -eq $ProductVersion) (Get-Value $entry 'DisplayVersion')
        Add-Result '1 install' 'entry publisher' ((Get-Value $entry 'Publisher') -ceq 'Asteski') (Get-Value $entry 'Publisher')
        Add-Result '1 install' 'entry project link' ((Get-Value $entry 'URLInfoAbout') -ceq $ProjectUrl) (Get-Value $entry 'URLInfoAbout')
        Add-Result '1 install' 'entry install location' ((Get-Value $entry 'InstallLocation') -in @($null, '', $installDir, "$installDir\")) (Get-Value $entry 'InstallLocation')
        Add-Info '1 install' 'entry icon' ("DisplayIcon=" + (Get-Value $entry 'DisplayIcon'))
    }
    $runValue = Get-ItemProperty -LiteralPath $runKey -Name 'WinXCornersPlus' -ErrorAction SilentlyContinue
    Add-Result '1 install' 'no autostart (Run) value was added' ($null -eq $runValue)
    Add-Result '1 install' 'no HKLM\SOFTWARE\WinXCornersPlus key was added' (-not (Test-Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus'))
    Add-Result '1 install' 'nothing added to PATH' (([Environment]::GetEnvironmentVariable('Path', 'Machine')) -notlike '*WinXCornersPlus*')
    Add-Result '1 install' 'app not started by the installer' (@(Get-AppProcesses).Count -eq 0)

    # ---- 2. ACL -------------------------------------------------------------------------------------------
    if (Test-Path -LiteralPath $installDir) {
        $acl = Test-WriteTypeAllowRules $installDir
        Add-Result '2 acl' 'no write-type Allow rule for Everyone / Authenticated Users / Users / INTERACTIVE' ($acl.Offending.Count -eq 0) `
            (($acl.Offending | ForEach-Object { "$($_.IdentityReference.Value): $($_.FileSystemRights)" }) -join '; ')
        $adminOwners = @('S-1-5-32-544', 'S-1-5-18', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')
        Add-Result '2 acl' 'owner is Administrators, SYSTEM or TrustedInstaller' ($acl.Owner -in $adminOwners) $acl.Owner
        foreach ($file in 'WinXCornersPlus.exe', 'WinXCornersPlus.dll') {
            $fileAcl = Test-WriteTypeAllowRules (Join-Path $installDir $file)
            Add-Result '2 acl' "$file not writable by non-administrators" ($fileAcl.Offending.Count -eq 0)
        }
        $probe = Invoke-AppProbe $installDir
        Add-Result '2 acl' "app's InstallLocationProbe says the install folder is not writable" ($probe -eq 'false') $probe
        $control = Join-Path ([System.IO.Path]::GetTempPath()) ('wxc-writable-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $control | Out-Null
        $acl = Get-Acl -LiteralPath $control
        $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
                [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'Modify', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
        Set-Acl -LiteralPath $control -AclObject $acl
        $controlProbe = Invoke-AppProbe $control
        Remove-Item -LiteralPath $control -Recurse -Force
        Add-Result '2 acl' 'control: the probe does flag a folder that Users may modify' ($controlProbe -eq 'true') $controlProbe
    }

    # ---- 3. Run -------------------------------------------------------------------------------------------
    $app = Start-AppAsStandardUser '3 run'
    if ($app) {
        $machine = Get-ProcessMachineName -ProcessId $app.Id
        Add-Result '3 run' 'process runs natively as the package architecture' ($machine -eq $Architecture) $machine
        $windows = Send-CloseAppEndSession -ProcessId $app.Id
        $code = Wait-Exit $app 15
        Add-Result '3 run' 'exits cleanly when asked the way the installer asks' ($code -eq 0) "windows asked: $windows; exit code: $code"
        if ($null -eq $code) { $app.Kill($true) }
    }

    # ---- 4. Upgrade while running --------------------------------------------------------------------------
    $app = Start-AppAsStandardUser '4 upgrade'
    $run = Invoke-Msiexec '4-upgrade' @('/i', "`"$UpgradeMsi`"")
    Add-Result '4 upgrade' 'upgrade exits with 0 (no reboot: not 3010)' ($run.ExitCode -eq 0) "exit code $($run.ExitCode)"
    $arp = @(Get-ArpEntries)
    Add-Result '4 upgrade' 'exactly one Add/Remove Programs entry' ($arp.Count -eq 1) "$($arp.Count) entries"
    Add-Result '4 upgrade' 'entry has the new version' ($arp.Count -eq 1 -and (Get-Value $arp[0] 'DisplayVersion') -eq $UpgradeProductVersion) `
        (($arp | ForEach-Object { Get-Value $_ 'DisplayVersion' }) -join ', ')
    if ($app) {
        $code = Wait-Exit $app 5
        Add-Result '4 upgrade' 'the running old copy is gone' ($null -ne $code) "exit code: $code"
        Add-Info '4 upgrade' 'how it was closed' $(if ($code -eq 0) { 'clean exit on WM_ENDSESSION (exit code 0)' } elseif ($code -eq 1) { 'terminated by the CloseApplication fallback (exit code 1)' } else { "exit code $code" })
        if ($null -eq $code) { $app.Kill($true) }
    }
    Add-Result '4 upgrade' 'the app was not restarted by the installer' (@(Get-AppProcesses).Count -eq 0)
    Add-Result '4 upgrade' 'files still installed' (Test-Path -LiteralPath $installedExe)
    $rebootLines = @(($run.Text -split "`r?`n") | Where-Object { $_ -match 'RESTART MANAGER|ReplacedInUseFiles|reboot' } | Select-Object -First 12)
    Add-Info '4 upgrade' 'msiexec log, restart/reboot lines' ($rebootLines -join ' || ')

    # ---- 5. Downgrade -------------------------------------------------------------------------------------
    $run = Invoke-Msiexec '5-downgrade' @('/i', "`"$Msi`"")
    Add-Result '5 downgrade' 'older package is refused (exit 1603)' ($run.ExitCode -eq 1603) "exit code $($run.ExitCode)"
    Add-Result '5 downgrade' 'with the downgrade message' ([bool] ($run.Text -and $run.Text.Contains($downgradeText)))
    $arp = @(Get-ArpEntries)
    Add-Result '5 downgrade' 'newer version still installed' ($arp.Count -eq 1 -and (Get-Value $arp[0] 'DisplayVersion') -eq $UpgradeProductVersion -and (Test-Path -LiteralPath $installedExe))

    # ---- 6. Uninstall while running -----------------------------------------------------------------------
    $approvals = 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus\ElevatedApprovals'
    $hklm = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', 'Registry64')
    $key = $hklm.CreateSubKey('SOFTWARE\WinXCornersPlus\ElevatedApprovals'); $key.SetValue('S-1-5-21-0-0-0-1000', 'test approval'); $key.Dispose(); $hklm.Dispose()
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKey -Name 'WinXCornersPlus' -Value "`"$installedExe`""
    if (-not (Test-Path -LiteralPath $settingsFile)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $settingsFile) | Out-Null
        Set-Content -LiteralPath $settingsFile -Value '{ "test": "kept on uninstall" }'
        $createdSettings = $true
    }
    Add-Info '6 uninstall' 'prepared' "HKLM approval: $(Test-Path $approvals); Run value: $($null -ne (Get-ItemProperty -LiteralPath $runKey -Name WinXCornersPlus -ErrorAction SilentlyContinue)); settings: $(Test-Path -LiteralPath $settingsFile)"
    $app = Start-AppAsStandardUser '6 uninstall'
    $run = Invoke-Msiexec '6-uninstall' @('/x', "`"$UpgradeMsi`"")
    Add-Result '6 uninstall' 'uninstall exits with 0 (no reboot: not 3010)' ($run.ExitCode -eq 0) "exit code $($run.ExitCode)"
    if ($app) {
        $code = Wait-Exit $app 5
        Add-Result '6 uninstall' 'the running copy is gone' ($null -ne $code) "exit code: $code"
        if ($null -eq $code) { $app.Kill($true) }
    }
    Add-Result '6 uninstall' 'program folder removed' (-not (Test-Path -LiteralPath $installDir))
    Add-Result '6 uninstall' 'Start-menu shortcut removed' (-not (Test-Path -LiteralPath $shortcut))
    Add-Result '6 uninstall' 'Add/Remove Programs entry removed' (@(Get-ArpEntries).Count -eq 0)
    Add-Result '6 uninstall' 'HKLM\SOFTWARE\WinXCornersPlus removed' (-not (Test-Path 'Registry::HKEY_LOCAL_MACHINE\SOFTWARE\WinXCornersPlus'))
    Add-Result '6 uninstall' 'HKCU Run value WinXCornersPlus removed' ($null -eq (Get-ItemProperty -LiteralPath $runKey -Name 'WinXCornersPlus' -ErrorAction SilentlyContinue))
    Add-Result '6 uninstall' "$settingsFile kept" (Test-Path -LiteralPath $settingsFile)
    Add-Result '6 uninstall' 'no app process left' (@(Get-AppProcesses).Count -eq 0)

    # ---- 7. Missing runtime ---------------------------------------------------------------------------------
    $run = Invoke-Msiexec '7-no-runtime' @('/i', "`"$NoRuntimeMsi`"")
    Add-Result '7 no runtime' 'install stops (exit 1603)' ($run.ExitCode -eq 1603) "exit code $($run.ExitCode)"
    Add-Result '7 no runtime' 'with the prerequisite message' ([bool] ($run.Text -and $run.Text.Contains($runtimeText)))
    Add-Result '7 no runtime' 'nothing installed' (-not (Test-Path -LiteralPath $installDir) -and @(Get-ArpEntries).Count -eq 0)
    $check = @(($run.Text -split "`r?`n") | Where-Object { $_ -match 'NetCoreCheck|WXC_DOTNET_DESKTOP_CHECK' } | Select-Object -First 6)
    Add-Info '7 no runtime' 'detection' ($check -join ' || ')
}
finally {
    Get-AppProcesses | ForEach-Object { Write-Host "Stopping leftover process $($_.Id)"; $_.Kill($true) }
    if ($createdSettings) { Remove-Item -LiteralPath (Split-Path -Parent $settingsFile) -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-StandardTestUser -Name $testUser

    $table = @('| Step | Check | Result | Detail |', '|---|---|---|---|') + @($results | ForEach-Object {
            "| $($_.Step) | $($_.Check) | $(if ($_.Result -eq 'FAIL') { '**FAIL**' } else { $_.Result }) | $(($_.Detail -replace '\|', '/') -replace '[\r\n]+', ' ') |" })
    $table | Out-File -LiteralPath (Join-Path $LogDirectory 'results.md') -Encoding utf8
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value (@("## Installer tests ($Architecture)", '') + $table + @(''))
    }
}

$failed = @($results | Where-Object Result -eq 'FAIL')
Write-Host "$($results.Count) results, $($failed.Count) failed."
if ($failed.Count -gt 0) { throw "$($failed.Count) installer check(s) failed: $(($failed | ForEach-Object { "$($_.Step): $($_.Check)" }) -join '; ')" }
