using System.Diagnostics;

namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>Facts about the machine the tests run on, used to skip (never to pass) what it cannot support.</summary>
internal static class TestEnvironment
{
    /// <summary>The test process has an elevated (administrator) token, as on the GitHub Windows images.</summary>
    internal static bool IsElevated => Environment.IsPrivilegedProcess;

    internal const string NotElevatedReason =
        "The test process is not elevated, so HKLM cannot be written (GitHub's Windows runners are elevated).";

    /// <summary>The desktop shell (explorer.exe) owns the shell window in this session.</summary>
    internal static bool TryGetDesktopShell(out int explorerProcessId)
    {
        explorerProcessId = 0;
        var shell = TestNative.GetShellWindow();
        if (shell == IntPtr.Zero)
        {
            return false;
        }

        TestNative.GetWindowThreadProcessId(shell, out var pid);
        if (pid == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (ArgumentException)
        {
            return false;
        }

        explorerProcessId = (int)pid;
        return true;
    }

    internal static string Describe()
    {
        return $"Windows build {Environment.OSVersion.Version.Build}, {(IsElevated ? "elevated" : "not elevated")}, " +
               $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} process, user interactive: {Environment.UserInteractive}";
    }
}
