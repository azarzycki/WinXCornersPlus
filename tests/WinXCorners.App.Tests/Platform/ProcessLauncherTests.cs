using System.Diagnostics;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// ProcessLauncher and DeElevatedLauncher with real child processes. Children are cmd.exe / powershell.exe runs that
/// only write a marker file into a test folder and exit.
/// </summary>
public sealed class ProcessLauncherTests : IDisposable
{
    private static readonly TimeSpan ChildTimeout = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;
    private readonly TempDirectory _temp = new();
    private readonly BoundedLog _log = new();

    public ProcessLauncherTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        foreach (var line in _log.Snapshot())
        {
            _output.WriteLine(line);
        }

        _temp.Dispose();
    }

    /// <summary>Real system folders, except that custom commands start in the test folder.</summary>
    private SystemDirectories Directories => SystemDirectoriesProvider.Get() with { UserProfile = _temp.Path };

    private static string CmdArguments(string script) => "/d /c " + script;

    private static LaunchRequest CustomCmd(string script, bool hidden = false)
        => new("cmd.exe", CmdArguments(script), hidden, LaunchTargetKind.CustomCommand);

    [Fact]
    public void A_custom_command_runs_in_the_planned_working_directory()
    {
        var marker = _temp.File("cd.txt");
        var launcher = new ProcessLauncher(_log, Directories, isElevated: false);

        var launched = launcher.TryLaunch(CustomCmd($"echo %CD%> \"{marker}\"& echo here> relative.txt"), out var error);

        Assert.True(launched, error);
        Assert.Null(error);
        var text = Poll.WaitForFile(marker, ChildTimeout);
        Assert.NotNull(text);
        Assert.EndsWith(Path.GetFileName(_temp.Path), text.Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(Poll.WaitForFile(_temp.File("relative.txt"), ChildTimeout));
        Assert.Contains(_log.Snapshot(), line => line.Contains("] Launched ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hidden_custom_command_still_runs()
    {
        var marker = _temp.File("hidden.txt");
        var launcher = new ProcessLauncher(_log, Directories, isElevated: false);

        Assert.True(launcher.TryLaunch(CustomCmd($"echo hidden> \"{marker}\"", hidden: true), out var error), error);

        Assert.NotNull(Poll.WaitForFile(marker, ChildTimeout));
        Assert.Contains(_log.Snapshot(), line => line.Contains("(hidden)", StringComparison.Ordinal));
    }

    public static TheoryData<string, LaunchRequest> RefusedRequests => new()
    {
        { "plain http link", new LaunchRequest("http://example.com/", string.Empty, false, LaunchTargetKind.Link) },
        { "link with user info", new LaunchRequest("https://github.com@evil.example/", string.Empty, false, LaunchTargetKind.Link) },
        { "relative built-in", new LaunchRequest("cmd.exe", "/c echo x> refused.txt", false, LaunchTargetKind.BuiltIn) },
        { "built-in outside Windows", new LaunchRequest(@"C:\Users\Public\cmd.exe", string.Empty, false, LaunchTargetKind.BuiltIn) },
        { "device path built-in", new LaunchRequest(@"\\?\C:\Windows\System32\cmd.exe", string.Empty, false, LaunchTargetKind.BuiltIn) },
        { "control character", new LaunchRequest("cmd.exe", "/c echo x\u0007> refused.txt", false, LaunchTargetKind.CustomCommand) },
        { "quote in target", new LaunchRequest("cmd.exe\" /c echo x> refused.txt", string.Empty, false, LaunchTargetKind.CustomCommand) },
        { "empty target", new LaunchRequest("   ", string.Empty, false, LaunchTargetKind.CustomCommand) },
    };

    [Theory]
    [MemberData(nameof(RefusedRequests))]
    public void Requests_the_planner_rejects_are_refused_and_start_nothing(string why, LaunchRequest request)
    {
        foreach (var elevated in new[] { false, true })
        {
            var deElevatedCalls = 0;
            var launcher = new ProcessLauncher(_log, Directories, elevated, (string _, string _, string _, bool _, out string? e) =>
            {
                deElevatedCalls++;
                e = null;
                return true;
            });

            Assert.False(launcher.TryLaunch(request, out var error), why);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(0, deElevatedCalls);
        }

        Thread.Sleep(500);
        Assert.False(File.Exists(_temp.File("refused.txt")), "A refused request started a process.");
        Assert.Contains(_log.Snapshot(), line => line.Contains("] Launch refused:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_program_fails_without_throwing_or_showing_UI()
    {
        var launcher = new ProcessLauncher(_log, Directories, isElevated: false);

        var launched = launcher.TryLaunch(
            new LaunchRequest(_temp.File("does-not-exist.exe"), string.Empty, false, LaunchTargetKind.CustomCommand), out var error);

        Assert.False(launched);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Contains(_log.Snapshot(), line => line.Contains("] Launch failed:", StringComparison.Ordinal));
    }

    public static TheoryData<string> RequestKinds => new() { "built-in", "custom", "link", "settings" };

    private LaunchRequest RequestOfKind(string kind, string marker) => kind switch
    {
        "built-in" => SystemTargets.FileExplorer(Directories),
        "custom" => CustomCmd($"echo leaked> \"{marker}\"", hidden: true),
        "link" => SystemTargets.Link("https://github.com/asteski/WinXCornersPlus"),
        _ => SystemTargets.SystemSettings()
    };

    [Theory]
    [MemberData(nameof(RequestKinds))]
    public void When_elevated_every_kind_goes_through_the_de_elevated_path(string kind)
    {
        var calls = new List<(string File, string Arguments, string WorkingDirectory, bool Hidden)>();
        var launcher = new ProcessLauncher(_log, Directories, isElevated: true, (string file, string arguments, string workingDirectory, bool hidden, out string? e) =>
        {
            calls.Add((file, arguments, workingDirectory, hidden));
            e = null;
            return true;
        });
        var request = RequestOfKind(kind, _temp.File("leak.txt"));

        Assert.True(launcher.TryLaunch(request, out var error), error);

        var call = Assert.Single(calls);
        Assert.True(LaunchPlanner.TryPlan(request, true, Directories, out var plan, out _));
        Assert.Equal(plan.FileName, call.File);
        Assert.Equal(plan.Arguments, call.Arguments);
        Assert.Equal(plan.WorkingDirectory, call.WorkingDirectory);
        Assert.Equal(plan.Hidden, call.Hidden);
        Assert.Contains(_log.Snapshot(), line => line.Contains("without administrator rights", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(RequestKinds))]
    public void When_elevated_and_the_de_elevated_path_fails_nothing_is_started(string kind)
    {
        var marker = _temp.File("leak.txt");
        var launcher = new ProcessLauncher(_log, Directories, isElevated: true, (string _, string _, string _, bool _, out string? e) =>
        {
            e = "no shell";
            return false;
        });

        Assert.False(launcher.TryLaunch(RequestOfKind(kind, marker), out var error));

        Assert.Equal("no shell", error);
        if (kind == "custom")
        {
            Thread.Sleep(2000);
            Assert.False(File.Exists(marker), "The launch fell back to starting the program with this process's token.");
        }
    }

    [Fact]
    public void De_elevated_launch_of_nothing_fails_cleanly()
    {
        Assert.False(DeElevatedLauncher.TryShellExecute("  ", string.Empty, string.Empty, false, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>
    /// The de-elevation mechanism: the shell (explorer.exe) performs the ShellExecute, so the child is explorer's child
    /// and gets explorer's token. Without a desktop shell the call must fail and start nothing (fail closed).
    /// </summary>
    [SkippableFact]
    public void De_elevated_launch_is_performed_by_the_desktop_shell_or_fails_closed()
    {
        var marker = _temp.File("parent.txt");
        var script = "$p = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID)).ParentProcessId; " +
                     $"Set-Content -LiteralPath '{marker}' -Value $p";
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"";
        var hasShell = TestEnvironment.TryGetDesktopShell(out var shellProcessId);
        _output.WriteLine($"{TestEnvironment.Describe()}; desktop shell: {(hasShell ? $"explorer.exe pid {shellProcessId}" : "none")}");

        string? error = null;
        var launched = Sta.Run(() => DeElevatedLauncher.TryShellExecute(powershell, arguments, _temp.Path, hidden: true, out error));
        _output.WriteLine($"launched: {launched}, error: {error}");

        if (!hasShell)
        {
            Assert.False(launched, "Without a desktop shell the de-elevated launch must fail.");
            Assert.False(string.IsNullOrWhiteSpace(error));
            Thread.Sleep(3000);
            Assert.False(File.Exists(marker), "Something was started although the shell could not be reached.");
            throw new SkipException("No desktop shell (explorer.exe) in this session: only the fail-closed half was verified.");
        }

        if (!launched && error is not null && error.Contains("(0x00000001)", StringComparison.Ordinal))
        {
            // explorer.exe answered IShellWindows.FindWindowSW with S_FALSE: it runs, but no desktop view is registered
            // in this session (seen on the windows-11-arm image). The launcher must then fail closed.
            Thread.Sleep(3000);
            Assert.False(File.Exists(marker), "Something was started although the shell had no desktop to launch from.");
            throw new SkipException(
                "explorer.exe runs but has not registered a desktop view with IShellWindows (S_FALSE) in this session; " +
                "only the fail-closed half was verified.");
        }

        Assert.True(launched, error);
        var text = Poll.WaitForFile(marker, ChildTimeout);
        Assert.NotNull(text);
        var parentId = int.Parse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture);
        using var parent = Process.GetProcessById(parentId);
        Assert.Equal("explorer", parent.ProcessName, ignoreCase: true);
    }
}
