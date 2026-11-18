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

    /// <summary>
    /// Appends one line to the file named by WXC_TEST_FACTS (set by CI); build/Write-TestSummary.ps1 puts those lines in
    /// the job summary. Facts are what the runner turned out to be (UAC state, shell elevation), not test results.
    /// </summary>
    internal static void RecordFact(string fact)
    {
        var path = Environment.GetEnvironmentVariable("WXC_TEST_FACTS");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (FactsLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, fact + Environment.NewLine);
        }
    }

    private static readonly Lock FactsLock = new();

    internal static string Describe()
    {
        return $"Windows build {Environment.OSVersion.Version.Build}, {(IsElevated ? "elevated" : "not elevated")}, " +
               $"{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture} process, user interactive: {Environment.UserInteractive}";
    }
}
