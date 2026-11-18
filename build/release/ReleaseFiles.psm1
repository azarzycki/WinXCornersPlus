<#
.SYNOPSIS
  File-system helpers shared by the release scripts (Windows only: they read PE version resources and
  Authenticode signatures). The rules they apply live in the pure ReleaseTools.psm1.
#>

Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1')

# The files whose version resources must carry the release version.
$script:StampedFiles = @('WinXCornersPlus.exe', 'WinXCornersPlus.dll')

function Assert-ReleaseVersionStamp {
    <#
    .SYNOPSIS
      Throws unless WinXCornersPlus.exe and .dll in -Directory carry exactly the release version
      (FileVersion = X.Y.Z.0, ProductVersion = the SemVer version, optionally +commit metadata).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $NumericVersion
    )
    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($name in $script:StampedFiles) {
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $problems.Add("$name is missing."); continue }
        $info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path)
        Write-Host ("{0}: FileVersion={1} ProductVersion={2}" -f $name, $info.FileVersion, $info.ProductVersion)
        foreach ($problem in (Test-VersionStamp -FileName $name -ExpectedVersion $Version -ExpectedNumericVersion $NumericVersion `
                    -FileVersion $info.FileVersion -ProductVersion $info.ProductVersion)) {
            $problems.Add($problem)
        }
    }
    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "::error::$_" }
        throw "Version stamp verification failed ($($problems.Count) problem(s))."
    }
    Write-Host "Version stamp verified: $Version ($NumericVersion)."
}

function Get-SignableFiles {
    <# The files code signing covers in -Directory: every .exe, .dll and .msi (recursively). #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Directory)
    @(Get-ChildItem -LiteralPath $Directory -Recurse -File |
        Where-Object { $_.Extension -in '.exe', '.dll', '.msi' } | Sort-Object FullName)
}

function Assert-AuthenticodeSigned {
    <#
    .SYNOPSIS
      Throws unless every .exe, .dll and .msi under -Directory has a valid Authenticode signature. With
      -ExpectedSubject, the signer certificate's subject must also contain that text.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [AllowEmptyString()] [string] $ExpectedSubject = ''
    )
    $files = @(Get-SignableFiles -Directory $Directory)
    if ($files.Count -eq 0) { throw "No .exe, .dll or .msi files under $Directory to verify." }
    $bad = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
        $subject = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { '(none)' }
        $stamped = if ($signature.TimeStamperCertificate) { 'timestamped' } else { 'NOT timestamped' }
        Write-Host ("{0,-10} {1} | {2} | {3}" -f $signature.Status, $file.Name, $subject, $stamped)
        if ($signature.Status -ne 'Valid') { $bad.Add("$($file.Name): $($signature.Status) $($signature.StatusMessage)"); continue }
        if (-not $signature.TimeStamperCertificate) { $bad.Add("$($file.Name): signature has no timestamp.") }
        if ($ExpectedSubject -and $subject -notlike "*$ExpectedSubject*") { $bad.Add("$($file.Name): signed by '$subject'.") }
    }
    if ($bad.Count -gt 0) {
        $bad | ForEach-Object { Write-Host "::error::$_" }
        throw "Authenticode verification failed for $($bad.Count) file(s)."
    }
    Write-Host "All $($files.Count) file(s) carry a valid, timestamped Authenticode signature."
}

# --- Processes (smoke and installer tests) ----------------------------------------------------------------

$script:NativeSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class WxcReleaseNative
{
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMachineInformation { public ushort ProcessMachine; public ushort Reserved; public uint MachineAttributes; }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(IntPtr process, int infoClass, out ProcessMachineInformation info, int size);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>The machine the process's code runs as (0x8664 x64, 0xAA64 ARM64), including emulated x64 on ARM64.</summary>
    public static int GetProcessMachine(int processId)
    {
        IntPtr handle = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        try
        {
            ProcessMachineInformation info;
            if (!GetProcessInformation(handle, 9 /* ProcessMachineTypeInfo */, out info, Marshal.SizeOf(typeof(ProcessMachineInformation))))
                throw new System.ComponentModel.Win32Exception();
            return info.ProcessMachine;
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>
    /// Sends what the Restart Manager and the installer's CloseApplication send: WM_QUERYENDSESSION and, when
    /// agreed, WM_ENDSESSION, both with ENDSESSION_CLOSEAPP, to every top-level window of the process.
    /// Returns the number of windows that got WM_ENDSESSION.
    /// </summary>
    public static int SendCloseAppEndSession(int processId)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, parameter) =>
        {
            int owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) windows.Add(window);
            return true;
        }, IntPtr.Zero);
        int ended = 0;
        foreach (var window in windows)
        {
            UIntPtr result;
            if (SendMessageTimeout(window, 0x0011, UIntPtr.Zero, new IntPtr(1), 0x0002 /* SMTO_ABORTIFHUNG */, 5000, out result) != IntPtr.Zero
                && result != UIntPtr.Zero)
            {
                SendMessageTimeout(window, 0x0016, new UIntPtr(1), new IntPtr(1), 0x0002, 5000, out result);
                ended++;
            }
        }
        return ended;
    }
}
'@

function Initialize-ReleaseNative {
    if (-not ('WxcReleaseNative' -as [type])) { Add-Type -TypeDefinition $script:NativeSource -Language CSharp }
}

function Get-PeMachine {
    <# The COFF machine field of a PE file: 0x8664 x64, 0xAA64 ARM64, 0x14C x86. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = [System.IO.BinaryReader]::new($stream)
        $stream.Position = 0x3C
        $stream.Position = $reader.ReadInt32()
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "$Path is not a PE file." }
        return [int] $reader.ReadUInt16()
    }
    finally { $stream.Dispose() }
}

function Get-MachineName([int] $Machine) {
    switch ($Machine) { 0x8664 { 'x64' } 0xAA64 { 'arm64' } 0x14C { 'x86' } default { '0x{0:X}' -f $Machine } }
}

function Get-ProcessMachineName {
    <# x64 or arm64: what the process actually runs as (x64 emulation on ARM64 reports x64). #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [int] $ProcessId)
    Initialize-ReleaseNative
    return Get-MachineName ([WxcReleaseNative]::GetProcessMachine($ProcessId))
}

function Send-CloseAppEndSession {
    <# Asks a WinXCornersPlus process to exit the way the installer does. Returns the number of windows that agreed. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)] [int] $ProcessId)
    Initialize-ReleaseNative
    return [WxcReleaseNative]::SendCloseAppEndSession($ProcessId)
}

Export-ModuleMember -Function Assert-ReleaseVersionStamp, Get-SignableFiles, Assert-AuthenticodeSigned, Get-PeMachine,
    Get-MachineName, Get-ProcessMachineName, Send-CloseAppEndSession
