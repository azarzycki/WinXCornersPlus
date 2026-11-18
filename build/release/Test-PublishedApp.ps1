<#
.SYNOPSIS
  Smoke test of a published WinXCornersPlus.exe on the runner that built it: the exe is a native image of the
  expected architecture, it starts (so it finds the .NET runtime), it is still running after a few seconds, it
  runs natively (not emulated), and it is stopped again.

.DESCRIPTION
  The app has no switch to redirect its settings (deliberately), and on GitHub's runners it runs elevated, so it
  stops at the first-run "approve settings for administrator sessions" dialog: that is a live, initialised
  process, which is what this test needs. Anything the app creates in the runner's profile that did not exist
  before (%APPDATA%\WinXCornersPlus, %LOCALAPPDATA%\WinXCornersPlus) is removed afterwards.

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
$profileFolders = @(
    (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'WinXCornersPlus'),
    (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'WinXCornersPlus'))
$preexisting = @($profileFolders | Where-Object { Test-Path -LiteralPath $_ })

$process = $null
try {
    $process = Start-Process -FilePath $exe -WorkingDirectory ([Environment]::SystemDirectory) -PassThru
    Write-Host "Started process $($process.Id)."
    if ($process.WaitForExit($AliveSeconds * 1000)) {
        throw "WinXCornersPlus.exe exited after start with code $($process.ExitCode) (is the .NET 10 Desktop Runtime for $Architecture installed?)."
    }
    $process.Refresh()
    $module = $process.MainModule.FileName
    $processMachine = Get-ProcessMachineName -ProcessId $process.Id
    Write-Host "Alive after $AliveSeconds s; main module $module; runs as $processMachine; $($process.Modules.Count) modules loaded."
    if (-not [string]::Equals([System.IO.Path]::GetFullPath($module), $exe, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The main module is $module, expected $exe."
    }
    if ($processMachine -ne $Architecture) { throw "The process runs as $processMachine, expected native $Architecture." }
    $runtime = @($process.Modules | Where-Object ModuleName -ieq 'coreclr.dll' | ForEach-Object FileName)
    Write-Host "CoreCLR: $($runtime -join ', ')"
    if ($runtime.Count -eq 0) { throw 'The .NET runtime (coreclr.dll) is not loaded in the process.' }
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value @(
            "## Smoke test ($Architecture)", '',
            "Started ``$module``: alive after $AliveSeconds s, native ``$processMachine`` process, runtime ``$($runtime[0])``.", '')
    }
}
finally {
    if ($process -and -not $process.HasExited) {
        $process.Kill($true)
        $process.WaitForExit(10000) | Out-Null
        Write-Host 'Stopped.'
    }
    foreach ($folder in $profileFolders) {
        if ($folder -notin $preexisting -and (Test-Path -LiteralPath $folder)) {
            Remove-Item -LiteralPath $folder -Recurse -Force
            Write-Host "Removed $folder, created by the test."
        }
    }
}
