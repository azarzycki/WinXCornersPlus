using System.Diagnostics;
using WinXCorners.App.Tests.Infrastructure;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.EndToEnd;

/// <summary>
/// Starts the real WinXCornersPlus.exe. The app has no switch to redirect its settings folder (deliberately: an
/// elevated instance must not take its settings location from its caller), so it uses the runner account's real
/// %APPDATA% and the real single-instance names; the test therefore skips when a copy is already running.
/// On an elevated runner the first copy stops at the elevated-session approval dialog (no approval is recorded
/// for the runner account), which is still a running first instance.
/// </summary>
public sealed class ExecutableSmokeTests
{
    private readonly ITestOutputHelper _output;

    public ExecutableSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string? FindExecutable()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "WinXCornersPlus.exe");
        if (File.Exists(beside))
        {
            return beside;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WinXCorners.Rewrite.slnx")))
        {
            directory = directory.Parent;
        }

        var bin = directory is null ? null : Path.Combine(directory.FullName, "src", "WinXCorners.App", "bin");
        return bin is null || !Directory.Exists(bin)
            ? null
            : Directory.EnumerateFiles(bin, "WinXCornersPlus.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
    }

    private Process Start(string exe, string workingDirectory, string name)
    {
        var process = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        })!;
        process.ErrorDataReceived += (_, e) => Record(name, e.Data);
        process.OutputDataReceived += (_, e) => Record(name, e.Data);
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        return process;
    }

    private readonly List<string> _childOutput = [];

    private void Record(string name, string? line)
    {
        if (line is not null)
        {
            lock (_childOutput)
            {
                _childOutput.Add($"[{name}] {line}");
            }
        }
    }

    private void WriteDiagnostics()
    {
        lock (_childOutput)
        {
            _childOutput.ForEach(_output.WriteLine);
        }

        var crashLog = CrashReporter.DefaultCrashLogPath();
        if (File.Exists(crashLog))
        {
            _output.WriteLine("crash.log:");
            _output.WriteLine(File.ReadAllText(crashLog));
        }
    }

    private static void KillTree(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            process.Dispose();
        }
    }

    [SkippableFact]
    public void The_built_exe_stays_running_and_a_second_start_exits_with_code_0()
    {
        Skip.If(Mutex.TryOpenExisting(SingleInstance.DefaultMutexName, out var existing) && DisposeAndTrue(existing),
            "A copy of WinXCornersPlus is already running in this session; the smoke test would interfere with it.");
        var exe = FindExecutable();
        Skip.If(exe is null, "WinXCornersPlus.exe was not found beside the tests or under src/WinXCorners.App/bin (build the app first).");
        _output.WriteLine($"{TestEnvironment.Describe()}; exe: {exe}");

        using var temp = new TempDirectory();
        Process? first = null;
        Process? second = null;
        try
        {
            first = Start(exe, temp.Path, "first");
            Assert.False(first.WaitForExit(5_000), $"The first copy exited after start with code {SafeExitCode(first)}.");

            if (TestEnvironment.IsElevated)
            {
                AssertTheElevatedGateHoldsEverythingBack(first);
            }

            second = Start(exe, temp.Path, "second");
            Assert.True(second.WaitForExit(10_000), "The second copy did not exit within 10 s.");
            Assert.Equal(0, second.ExitCode);

            Assert.False(first.HasExited, "Starting a second copy ended the first one.");
        }
        finally
        {
            KillTree(second);
            KillTree(first);
            WriteDiagnostics();
        }
    }

    /// <summary>
    /// The real executable started elevated (the runner is an administrator) stops at the approval dialog: no approval
    /// of the current format is on record for the runner account. While that dialog shows, the hidden main window
    /// must not exist. It is the window that registers raw input (when its handle is created) and owns the tray icon,
    /// so its absence shows that neither exists before the gate has passed (review V2 test gap). A process cannot list
    /// another process's raw-input registrations or tray icons directly; the main window, which carries the app's name
    /// as its title, is the observable stand-in. Also checks that the elevated process turned on Redirection Guard.
    /// </summary>
    private void AssertTheElevatedGateHoldsEverythingBack(Process first)
    {
        List<(string Title, string Class, bool Visible)> windows = [];
        var found = Poll.Until(() =>
        {
            windows = TestNative.TopLevelWindowsOf(first.Id);
            return windows.Any(static w => w.Visible && w.Title == ApprovalDialogTitle);
        }, TimeSpan.FromSeconds(20));
        foreach (var window in windows)
        {
            _output.WriteLine($"window of the first copy: \"{window.Title}\" class {window.Class} visible {window.Visible}");
        }

        Assert.True(found, "The elevated first copy did not show the administrator approval dialog.");
        Assert.DoesNotContain(windows, static w => w.Title is MainForm.TrayText or MainForm.TrayTextElevated);
        TestEnvironment.RecordFact($"Smoke test: elevated exe stopped at the approval dialog with {windows.Count} top-level windows, none of them the main window.");

        var supported = TestNative.GetProcessMitigationPolicy(Process.GetCurrentProcess().Handle, RedirectionTrustPolicy, out _, (UIntPtr)sizeof(uint));
        var childFlags = 0u;
        var read = supported && TestNative.GetProcessMitigationPolicy(first.Handle, RedirectionTrustPolicy, out childFlags, (UIntPtr)sizeof(uint));
        TestEnvironment.RecordFact($"Redirection Guard: {(supported ? $"supported; elevated exe flags 0x{childFlags:X}" : "not supported on this Windows build")}.");
        if (supported)
        {
            Assert.True(read, "The elevated exe's mitigation policy could not be read.");
            Assert.True((childFlags & 1) != 0, $"Redirection Guard is not enforced in the elevated exe (flags 0x{childFlags:X}).");
        }
    }

    private const string ApprovalDialogTitle = "WinXCornersPlus administrator approval";

    private const int RedirectionTrustPolicy = 16;

    private static bool DisposeAndTrue(IDisposable disposable)
    {
        disposable.Dispose();
        return true;
    }

    private static string SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "?";
        }
    }
}
