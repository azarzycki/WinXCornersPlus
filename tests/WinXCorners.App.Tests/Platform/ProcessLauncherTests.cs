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
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.NotElevated);

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
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.NotElevated);

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
            var launcher = new ProcessLauncher(_log, Directories, elevated ? ElevationMode.ElevatedWithStandardShell : ElevationMode.NotElevated, (string _, string _, string _, bool _, out string? e) =>
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
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.NotElevated);

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
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.ElevatedWithStandardShell, (string file, string arguments, string workingDirectory, bool hidden, out string? e) =>
        {
            calls.Add((file, arguments, workingDirectory, hidden));
            e = null;
            return true;
        });
        var request = RequestOfKind(kind, _temp.File("leak.txt"));

        Assert.True(launcher.TryLaunch(request, out var error), error);

        var call = Assert.Single(calls);
        Assert.True(LaunchPlanner.TryPlan(request, ElevationMode.ElevatedWithStandardShell, Directories, out var plan, out _));
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
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.ElevatedWithStandardShell, (string _, string _, string _, bool _, out string? e) =>
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
    /// Review V2 D4, the token check itself: this process's elevation state as the app classifies it, and the desktop
    /// shell's own token, must agree with each other. On an account with User Account Control on (a split token) the
    /// shell must not be elevated; with UAC off or on the built-in Administrator account, everything is elevated, and
    /// the shell check must say so. The runner's state is written to the job summary.
    /// </summary>
    [SkippableFact]
    public void The_elevation_state_and_the_shell_token_check_agree()
    {
        var type = ProcessElevation.CurrentTokenType();
        var mode = ProcessElevation.CurrentMode;
        var hasShell = TestEnvironment.TryGetDesktopShell(out var shellProcessId);
        var shellElevated = hasShell ? ProcessElevation.IsProcessElevated(shellProcessId) : null;
        var check = DeElevatedLauncher.CheckShellToken(IntPtr.Zero);
        var fact = $"Elevation: test process elevated={ProcessElevation.IsElevated}, token type={type}, app state={mode}; " +
                   $"desktop shell: {(hasShell ? $"explorer.exe pid {shellProcessId}, elevated={shellElevated?.ToString() ?? "unknown"}" : "none")}; " +
                   $"shell check: {check ?? "passes (launches go through the shell)"}";
        _output.WriteLine(fact);
        TestEnvironment.RecordFact(fact);

        Assert.Equal(ElevationModes.Classify(Environment.IsPrivilegedProcess, type), mode);
        Assert.False(ProcessElevation.IsProcessElevated(-1).HasValue);
        Assert.False(ProcessElevation.IsProcessElevated(int.MaxValue - 3).HasValue);
        Assert.Equal(ProcessElevation.IsElevated, ProcessElevation.IsProcessElevated(Environment.ProcessId));
        Skip.IfNot(hasShell, "No desktop shell (explorer.exe) in this session, so there is no shell token to check.");

        Assert.NotNull(shellElevated);
        if (shellElevated == true)
        {
            Assert.Contains("itself runs as administrator", check, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(check);
        }

        if (mode == ElevationMode.ElevatedWithoutStandardRights)
        {
            // No split token while elevated: UAC is off or this is the built-in Administrator, so explorer is elevated too.
            Assert.True(shellElevated, "With no normal-rights context on the account the shell should be elevated as well.");
        }
        else if (mode == ElevationMode.NotElevated)
        {
            Assert.False(shellElevated, "A normal-rights process found an elevated shell.");
        }
    }

    /// <summary>
    /// The de-elevation mechanism end to end (review V2 test gap: the child's token was never checked). The shell
    /// (explorer.exe) performs the ShellExecute, so the child is explorer's child and gets explorer's token: it must
    /// NOT be elevated. Where the shell itself is elevated, or there is no desktop shell, the call must fail and start
    /// nothing (fail closed).
    /// </summary>
    [SkippableFact]
    public void De_elevated_launch_gives_the_child_a_non_elevated_token_or_fails_closed()
    {
        var marker = _temp.File("child.txt");
        var script = "$p = (Get-CimInstance Win32_Process -Filter ('ProcessId=' + $PID)).ParentProcessId; " +
                     $"Set-Content -LiteralPath '{marker}' -Value (\"$PID $p\"); Start-Sleep -Seconds 30";
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"";
        var hasShell = TestEnvironment.TryGetDesktopShell(out var shellProcessId);
        var shellElevated = hasShell ? ProcessElevation.IsProcessElevated(shellProcessId) : null;
        _output.WriteLine($"{TestEnvironment.Describe()}; desktop shell: {(hasShell ? $"explorer.exe pid {shellProcessId}, elevated={shellElevated}" : "none")}");

        string? error = null;
        var launched = Sta.Run(() => DeElevatedLauncher.TryShellExecute(powershell, arguments, _temp.Path, hidden: true, out error));
        _output.WriteLine($"launched: {launched}, error: {error}");

        if (!hasShell || shellElevated != false)
        {
            Assert.False(launched, "Without a normal-rights desktop shell the de-elevated launch must fail.");
            Assert.False(string.IsNullOrWhiteSpace(error));
            Thread.Sleep(3000);
            Assert.False(File.Exists(marker), "Something was started although the shell could not give it normal rights.");
            var reason = !hasShell ? "No desktop shell (explorer.exe) in this session" : "The desktop shell itself runs as administrator";
            TestEnvironment.RecordFact($"De-elevated launch: {reason}; it failed closed as required.");
            throw new SkipException($"{reason}: only the fail-closed half was verified.");
        }

        if (!launched && error is not null && error.Contains("(0x00000001)", StringComparison.Ordinal))
        {
            // explorer.exe answered IShellWindows.FindWindowSW with S_FALSE: it runs, but no desktop view is registered
            // in this session (seen on the windows-11-arm image). The launcher must then fail closed.
            Thread.Sleep(3000);
            Assert.False(File.Exists(marker), "Something was started although the shell had no desktop to launch from.");
            TestEnvironment.RecordFact("De-elevated launch: explorer.exe registered no desktop view (S_FALSE); it failed closed as required.");
            throw new SkipException(
                "explorer.exe runs but has not registered a desktop view with IShellWindows (S_FALSE) in this session; " +
                "only the fail-closed half was verified.");
        }

        Assert.True(launched, error);
        var text = Poll.WaitForFile(marker, ChildTimeout);
        Assert.NotNull(text);
        var ids = text.Trim().Split(' ').Select(static t => int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        using var child = Process.GetProcessById(ids[0]);
        try
        {
            var childElevated = ProcessElevation.IsProcessElevated(ids[0]);
            _output.WriteLine($"child pid {ids[0]}, parent {ids[1]}, child elevated: {childElevated}");
            TestEnvironment.RecordFact($"De-elevated launch: child pid {ids[0]} started by pid {ids[1]}, child token elevated={childElevated}.");
            Assert.Equal(false, childElevated);
            using var parent = Process.GetProcessById(ids[1]);
            Assert.Equal("explorer", parent.ProcessName, ignoreCase: true);
        }
        finally
        {
            child.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Without_a_standard_rights_context_launches_start_directly_and_say_so()
    {
        var marker = _temp.File("direct.txt");
        var deElevatedCalls = 0;
        var launcher = new ProcessLauncher(_log, Directories, ElevationMode.ElevatedWithoutStandardRights,
            (string _, string _, string _, bool _, out string? e) =>
            {
                deElevatedCalls++;
                e = null;
                return true;
            });

        Assert.True(launcher.TryLaunch(CustomCmd($"echo direct> \"{marker}\"", hidden: true), out var error), error);

        Assert.Equal(0, deElevatedCalls);
        Assert.NotNull(Poll.WaitForFile(marker, ChildTimeout));
        Assert.Contains(_log.Snapshot(), line => line.Contains("with administrator rights", StringComparison.Ordinal));
    }
}
