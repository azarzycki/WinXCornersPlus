using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Knows whether this process is elevated, and restarts a NOT elevated process elevated through the UAC prompt.
/// </summary>
internal static class ProcessElevation
{
    /// <summary>Win32 ERROR_CANCELLED: the user chose "No" on the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    internal static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>This process's elevation in Core's three states (review V2 D4), from its own token.</summary>
    internal static ElevationMode CurrentMode => ElevationModes.Classify(IsElevated, CurrentTokenType());

    /// <summary>This process's TOKEN_ELEVATION_TYPE, or Unknown when it cannot be read.</summary>
    internal static TokenElevationType CurrentTokenType()
    {
        using var process = Process.GetCurrentProcess();
        return TryReadToken(process.SafeHandle, NativeMethods.TokenElevationTypeClass, out var value)
            ? (TokenElevationType)value
            : TokenElevationType.Unknown;
    }

    /// <summary>
    /// Whether the process <paramref name="processId"/> runs with an elevated token (TokenElevation). Null when it
    /// cannot be determined (the process is gone, or its token cannot be opened): callers treat that as "maybe".
    /// </summary>
    internal static bool? IsProcessElevated(int processId)
    {
        if (processId <= 0)
        {
            return null;
        }

        using var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process.IsInvalid)
        {
            return null;
        }

        return TryReadToken(process, NativeMethods.TokenElevationClass, out var elevated) ? elevated != 0 : null;
    }

    private static bool TryReadToken(SafeProcessHandle process, int informationClass, out int value)
    {
        value = 0;
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token))
        {
            token.Dispose();
            return false;
        }

        using (token)
        {
            return NativeMethods.GetTokenInformation(token, informationClass, out value, Marshal.SizeOf<int>(), out _);
        }
    }

    /// <summary>
    /// Starts this executable again with the <c>runas</c> verb. Returns false with a message when the user declines
    /// the prompt or the start fails; never throws for those.
    /// </summary>
    /// <remarks>
    /// Only a process that is NOT elevated may use this (review V2 D5). An elevated process gets no UAC prompt from
    /// <c>runas</c>, so the program files (which programs without administrator rights may have replaced) would start
    /// elevated silently. Core's RestartPolicy never asks for it then; this check fails closed if anything else does.
    /// </remarks>
    internal static bool TryRestartElevated(IReadOnlyList<string> arguments, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (IsElevated)
        {
            error = "WinXCornersPlus already runs as administrator, so Windows would not show its prompt; it was not restarted.";
            return false;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable) || !Path.IsPathFullyQualified(executable))
        {
            error = "The path of the running program is unknown, so it cannot be restarted.";
            return false;
        }

        var startInfo = new ProcessStartInfo
        {
            // Absolute path and System32 as the working directory: ShellExecute must not search the current
            // directory or the app folder for anything while starting an elevated process (review S3).
            FileName = executable,
            Arguments = CommandLineQuoting.Join(arguments),
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        try
        {
            using var process = Process.Start(startInfo);
            error = null;
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            error = "The administrator prompt was declined.";
            return false;
        }
        catch (Win32Exception ex)
        {
            error = $"Could not restart as administrator: {ex.Message}";
            return false;
        }
        catch (InvalidOperationException ex)
        {
            error = $"Could not restart as administrator: {ex.Message}";
            return false;
        }
        catch (PlatformNotSupportedException ex)
        {
            error = $"Could not restart as administrator: {ex.Message}";
            return false;
        }
    }
}
