using System.Diagnostics;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>ActionExecutor's real worker thread with fakes behind every side effect (no keys, launches or locks).</summary>
public sealed class ActionExecutorTests : IDisposable
{
    private const ushort VkTab = 0x09;
    private const ushort VkEscape = 0x1B;
    private const ushort VkLeftWin = 0x5B;
    private const ushort VkA = 0x41;
    private const ushort VkB = 0x42;
    private const ushort VkLeftShift = 0xA0;
    private const ushort VkRightControl = 0xA3;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;
    private readonly BoundedLog _log = new();
    private readonly FakeKeySender _keys = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeWindowQuery _windows = new();
    private readonly FakeSystemActions _system = new();
    private readonly SystemDirectories _directories = new(
        Windows: @"C:\Windows", System32: @"C:\Windows\System32", UserProfile: @"C:\Users\test", ProtectedRoots: [@"C:\Windows"]);
    private bool _taskViewForeground;
    private readonly ActionExecutor _executor;

    public ActionExecutorTests(ITestOutputHelper output)
    {
        _output = output;
        _executor = new ActionExecutor(_log, _directories, _keys, _launcher, _windows, () => _taskViewForeground, _system)
        {
            MainWindowHandle = new IntPtr(0x1234)
        };
    }

    public void Dispose()
    {
        _launcher.Gate?.Set();
        _executor.Dispose();
        foreach (var line in _log.Snapshot())
        {
            _output.WriteLine(line);
        }
    }

    private static ApplicationSettings WithCustom(string command, string parameters = "", bool hidden = false, bool enabled = true)
    {
        var settings = new ApplicationSettings { EnableCustomCommands = enabled };
        settings.CustomCommands[0].Command = command;
        settings.CustomCommands[0].Parameters = parameters;
        settings.CustomCommands[0].LaunchHidden = hidden;
        return settings;
    }

    private void Run(string actionId, ApplicationSettings? settings = null)
    {
        _executor.Execute(actionId, settings ?? new ApplicationSettings());
        Assert.True(_executor.WhenIdle().Wait(Wait), "The action did not finish.");
    }

    private IReadOnlyList<KeyEvent> SingleSend()
    {
        Assert.Single(_keys.Sent);
        Assert.True(_keys.Sent.TryPeek(out var events));
        return events;
    }

    [Theory]
    [InlineData(ActionIds.FileExplorer, @"C:\Windows\explorer.exe")]
    [InlineData(ActionIds.TaskManager, @"C:\Windows\System32\taskmgr.exe")]
    [InlineData(ActionIds.Settings, "ms-settings:")]
    public void Built_in_programs_reach_the_launcher_with_absolute_targets(string actionId, string target)
    {
        Run(actionId);

        var request = Assert.Single(_launcher.Requests);
        Assert.Equal(target, request.Target);
        Assert.Equal(LaunchTargetKind.BuiltIn, request.Kind);
        Assert.False(request.Hidden);
        Assert.Empty(_keys.Sent);
    }

    [Theory]
    [InlineData(ActionIds.ScreenSaver, SystemCommandKind.StartScreenSaver)]
    [InlineData(ActionIds.MonitorsOff, SystemCommandKind.TurnDisplayOff)]
    [InlineData(ActionIds.StartMenu, SystemCommandKind.OpenStartMenu)]
    public void System_commands_are_posted_to_the_main_window(string actionId, SystemCommandKind kind)
    {
        Run(actionId);

        var command = Assert.Single(_system.Commands);
        Assert.Equal(new IntPtr(0x1234), command.Window);
        Assert.Equal(kind, command.Kind);
        Assert.Equal(0, _system.Locks);
    }

    [Fact]
    public void Lock_screen_calls_the_workstation_lock_seam()
    {
        Run(ActionIds.LockScreen);

        Assert.Equal(1, _system.Locks);
        Assert.Empty(_system.Commands);
        Assert.Empty(_launcher.Requests);
    }

    [Theory]
    [InlineData(ActionIds.Desktop, (ushort)0x44)]
    [InlineData(ActionIds.ActionCenter, VkA)]
    [InlineData(ActionIds.NotificationCenter, (ushort)0x4E)]
    [InlineData(ActionIds.HideOtherWindows, (ushort)0x24)]
    public void Windows_key_shortcuts_are_sent_as_key_events(string actionId, ushort key)
    {
        Run(actionId);

        var events = SingleSend();
        Assert.Equal(new KeyEvent(VkLeftWin, false, true, KeyEventPlanner.DefaultDelayMs), events[0]);
        Assert.Contains(events, e => e.VirtualKey == key && !e.KeyUp);
        Assert.Equal(new KeyEvent(VkLeftWin, true, true, KeyEventPlanner.DefaultDelayMs), events[^1]);
    }

    [Fact]
    public void All_windows_toggles_Task_View_or_closes_it_with_Escape_when_it_is_in_front()
    {
        Run(ActionIds.AllWindows);
        var first = SingleSend();
        Assert.Contains(first, e => e.VirtualKey == VkTab);
        Assert.Contains(first, e => e.VirtualKey == VkLeftWin);

        _keys.Sent.Clear();
        _taskViewForeground = true;
        Run(ActionIds.AllWindows);
        var second = SingleSend();
        Assert.All(second, e => Assert.Equal(VkEscape, e.VirtualKey));
    }

    [Fact]
    public void Held_user_modifiers_are_released_before_the_sequence()
    {
        _keys.ModifiersDown = [VkRightControl, VkLeftShift];

        Run(ActionCatalog.CustomId(0), WithCustom("!a"));

        var events = SingleSend();
        Assert.Equal(4, events.Count);
        Assert.True(events[0].KeyUp && events[1].KeyUp);
        Assert.Equal(new HashSet<ushort> { VkRightControl, VkLeftShift }, new HashSet<ushort> { events[0].VirtualKey, events[1].VirtualKey });
        Assert.True(events.Single(e => e.VirtualKey == VkRightControl).Extended);
        Assert.Equal(new KeyEvent(VkA, false, false, KeyEventPlanner.DefaultDelayMs), events[2]);
        Assert.Equal(new KeyEvent(VkA, true, false, KeyEventPlanner.DefaultDelayMs), events[3]);
    }

    [Fact]
    public void A_custom_launch_reaches_the_launcher_with_its_parameters_and_hidden_flag()
    {
        Run(ActionCatalog.CustomId(0), WithCustom("notepad.exe", parameters: "C:\\notes.txt", hidden: true));

        var request = Assert.Single(_launcher.Requests);
        Assert.Equal(new LaunchRequest("notepad.exe", "C:\\notes.txt", true, LaunchTargetKind.CustomCommand), request);
        Assert.Contains(_log.Snapshot(), line => line.EndsWith(": started.", StringComparison.Ordinal));
    }

    [Fact]
    public void Disabled_or_invalid_custom_commands_do_nothing()
    {
        Run(ActionCatalog.CustomId(0), WithCustom("notepad.exe", enabled: false));
        Run(ActionCatalog.CustomId(0), WithCustom("!a+nosuchkey"));
        Run(ActionIds.None);
        Run("no-such-action");

        Assert.Empty(_launcher.Requests);
        Assert.Empty(_keys.Sent);
        Assert.Equal(4, _log.Snapshot().Count(line => line.Contains("nothing to do", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_conditional_command_consults_the_window_query_and_sends_the_matching_branch()
    {
        var settings = WithCustom("@[WXC.Class,Some title]:(a)?(b)");

        Run(ActionCatalog.CustomId(0), settings);
        Assert.Contains("exists(WXC.Class,Some title)", _windows.Calls);
        Assert.Contains(SingleSend(), e => e.VirtualKey == VkB);

        _keys.Sent.Clear();
        _windows.Existing.Add(("WXC.Class", "Some title"));
        Run(ActionCatalog.CustomId(0), settings);
        Assert.Contains(SingleSend(), e => e.VirtualKey == VkA);
    }

    [Fact]
    public void A_conditional_command_without_else_sends_nothing_when_no_condition_matches()
    {
        _windows.Foreground = new WindowInfo("Other", "Other");

        Run(ActionCatalog.CustomId(0), WithCustom("#[WXC.Class]:(a)"));

        Assert.Contains("foreground", _windows.Calls);
        Assert.Empty(_keys.Sent);
        Assert.Contains(_log.Snapshot(), line => line.Contains("no window condition matched", StringComparison.Ordinal));
    }

    [Fact]
    public void Work_happens_on_one_background_STA_thread_and_Execute_returns_at_once()
    {
        _launcher.Gate = new ManualResetEventSlim();
        var clock = Stopwatch.StartNew();
        _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        _executor.Execute(ActionIds.Desktop, new ApplicationSettings());
        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), "Execute blocked on the action.");

        Assert.True(_launcher.Entered.Wait(Wait));
        _launcher.Gate.Set();
        Assert.True(_executor.WhenIdle().Wait(Wait));

        Assert.True(_launcher.Threads.TryPeek(out var launchThread));
        Assert.True(_keys.Threads.TryPeek(out var keyThread));
        Assert.NotSame(Thread.CurrentThread, launchThread);
        Assert.Same(launchThread, keyThread);
        Assert.True(launchThread.IsBackground);
        Assert.Equal(ApartmentState.STA, launchThread.GetApartmentState());
    }

    [Fact]
    public void When_the_queue_is_full_further_actions_are_dropped_and_logged()
    {
        _launcher.Gate = new ManualResetEventSlim();
        _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        Assert.True(_launcher.Entered.Wait(Wait), "The first action never started.");

        // One action is running; the queue holds Capacity more; the next one is dropped.
        for (var i = 0; i < ActionExecutor.Capacity + 1; i++)
        {
            _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        }

        Assert.Single(_log.Snapshot(), line => line.Contains("dropped", StringComparison.Ordinal));
        _launcher.Gate.Set();
        Assert.True(_executor.WhenIdle().Wait(Wait));
        Assert.Equal(1 + ActionExecutor.Capacity, _launcher.Requests.Count);
    }

    [Fact]
    public void An_exception_in_one_action_is_logged_and_the_worker_keeps_going()
    {
        _launcher.ThrowNext = new InvalidOperationException("boom");

        Run(ActionIds.FileExplorer);
        Run(ActionIds.TaskManager);

        Assert.Contains(_log.Snapshot(), line => line.Contains("failed unexpectedly: InvalidOperationException: boom", StringComparison.Ordinal));
        Assert.Equal(2, _launcher.Requests.Count);
    }

    [Fact]
    public void WhenIdle_completes_at_once_when_nothing_is_queued()
    {
        Assert.True(_executor.WhenIdle().IsCompleted);
    }

    [Fact]
    public void Dispose_skips_queued_work_and_returns_within_its_time_limit()
    {
        _launcher.Gate = new ManualResetEventSlim();
        _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        Assert.True(_launcher.Entered.Wait(Wait));
        for (var i = 0; i < 3; i++)
        {
            _executor.Execute(ActionIds.TaskManager, new ApplicationSettings());
        }

        // Let the running action finish shortly after Dispose starts waiting.
        _ = Task.Delay(300).ContinueWith(_ => _launcher.Gate.Set(), TaskScheduler.Default);
        var clock = Stopwatch.StartNew();
        _executor.Dispose();
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Dispose took {clock.Elapsed}.");
        Assert.Single(_launcher.Requests);
        Assert.Equal(3, _log.Snapshot().Count(line => line.Contains("skipped: the app is shutting down", StringComparison.Ordinal)));

        _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        Assert.Contains(_log.Snapshot(), line => line.Contains("ignored: the app is shutting down", StringComparison.Ordinal));
        _executor.Dispose();
    }

    [Fact]
    public void Dispose_abandons_a_stuck_action_after_about_two_seconds()
    {
        _launcher.Gate = new ManualResetEventSlim();
        _executor.Execute(ActionIds.FileExplorer, new ApplicationSettings());
        Assert.True(_launcher.Entered.Wait(Wait));

        var clock = Stopwatch.StartNew();
        _executor.Dispose();
        clock.Stop();

        Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(5));
        Assert.Contains(_log.Snapshot(), line => line.Contains("did not finish within 2 seconds", StringComparison.Ordinal));
    }
}
