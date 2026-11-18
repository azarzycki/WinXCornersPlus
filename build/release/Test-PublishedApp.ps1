<#
.SYNOPSIS
  Smoke test of a published WinXCornersPlus.exe on the runner that built it: the exe is a native image of the
  expected architecture, it starts (so it finds the .NET runtime), it is still running after a few seconds, it
  runs natively (not emulated) with the runtime loaded, and it is stopped again.

.DESCRIPTION
  Runs on a disposable CI runner only. The app is started as a temporary local standard user, the way it normally
  runs: GitHub's runners run everything elevated, and an elevated first start would stop at the
  administrator-session approval dialog instead of starting the tray app. The user, its profile (and with it any
  settings the app wrote) and its processes are removed afterwards. Nothing in the runner account's profile is
  touched.

.EXAMPLE
  ./build/release/Test-PublishedApp.ps1 -Executable payload/WinXCornersPlus.exe -Architecture arm64
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Executable,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Architecture,
    [int] $AliveSeconds = 6
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseFiles.psm1') -Force

$exe = (Resolve-Path -LiteralPath $Executable).Path
$fileMachine = Get-MachineName (Get-PeMachine $exe)
Write-Host "OS architecture: $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture); exe image: $fileMachine"
if ($fileMachine -ne $Architecture) { throw "$exe is a $fileMachine image, expected $Architecture." }
if (Get-Process -Name WinXCornersPlus -ErrorAction SilentlyContinue) { throw 'A WinXCornersPlus process is already running on this runner.' }

# The standard user must be able to read the program (the workspace normally grants Users read access).
$acl = Get-Acl -LiteralPath (Split-Path -Parent $exe)
$acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
        [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-545'), 'ReadAndExecute', 'ContainerInherit, ObjectInherit', 'None', 'Allow'))
Set-Acl -LiteralPath (Split-Path -Parent $exe) -AclObject $acl

$user = 'wxcsmoketest'
$started = Get-Date
$credential = New-StandardTestUser -Name $user
$process = $null
try {
    $process = Start-ProcessAsUser -Credential $credential -FilePath $exe
    Write-Host "Started process $($process.Id) as $user."
    if ($process.WaitForExit($AliveSeconds * 1000)) {
        Write-AppCrashInformation -Since $started
        throw "WinXCornersPlus.exe exited after start with code $($process.ExitCode) (0x$('{0:X8}' -f $process.ExitCode))."
    }
    $image = Get-ProcessImagePath $process.Id
    $processMachine = Get-ProcessMachineName -ProcessId $process.Id
    $runtime = @($process.Modules | Where-Object ModuleName -ieq 'coreclr.dll' | ForEach-Object FileName)
    Write-Host "Alive after $AliveSeconds s; image $image; runs as $processMachine; CoreCLR: $($runtime -join ', ')"
    if (-not [string]::Equals($image, $exe, [System.StringComparison]::OrdinalIgnoreCase)) { throw "The process image is $image, expected $exe." }
    if ($processMachine -ne $Architecture) { throw "The process runs as $processMachine, expected native $Architecture." }
    if ($runtime.Count -eq 0) { throw 'The .NET runtime (coreclr.dll) is not loaded in the process.' }
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value @(
            "## Smoke test ($Architecture)", '',
            "Started ``$image`` as a standard user: alive after $AliveSeconds s, native ``$processMachine`` process, runtime ``$($runtime[0])``.", '')
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(10000) | Out-Null
        Write-Host 'Stopped.'
    }
    Remove-StandardTestUser -Name $user
}
