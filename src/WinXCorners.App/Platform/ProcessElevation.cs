using System.ComponentModel;
using System.Diagnostics;

namespace WinXCorners.App;

/// <summary>Knows whether this process is elevated and restarts it elevated through the UAC prompt.</summary>
internal static class ProcessElevation
{
    /// <summary>Win32 ERROR_CANCELLED: the user chose "No" on the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    internal static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>
    /// Starts this executable again with the <c>runas</c> verb. Returns false with a message when the user declines
    /// the prompt or the start fails; never throws for those.
    /// </summary>
    internal static bool TryRestartElevated(IReadOnlyList<string> arguments, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
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
