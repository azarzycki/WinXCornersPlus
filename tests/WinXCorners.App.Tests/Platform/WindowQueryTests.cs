using System.Drawing;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Input;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>WindowQuery and ForegroundWindowInspector against real top-level windows the tests create.</summary>
public sealed class WindowQueryTests
{
    private const string NoForegroundReason =
        "This session does not let the test process take the foreground (no interactive desktop or focus lock).";

    private readonly ITestOutputHelper _output;

    public WindowQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static TestWindow CreateCaptioned() =>
        TestWindow.Create(TestNative.WS_OVERLAPPEDWINDOW | TestNative.WS_VISIBLE, 0, new Rectangle(50, 50, 400, 300));

    [Fact]
    public void A_window_is_found_by_class_by_title_and_by_both()
    {
        Sta.Run(() =>
        {
            using var window = CreateCaptioned();
            var query = new WindowQuery();

            Assert.True(query.WindowExists(window.ClassName, null));
            Assert.True(query.WindowExists(null, window.Title));
            Assert.True(query.WindowExists(window.ClassName, window.Title));
        });
    }

    [Fact]
    public void Lookups_that_do_not_match_exactly_are_not_found()
    {
        Sta.Run(() =>
        {
            using var window = CreateCaptioned();
            var query = new WindowQuery();

            Assert.False(query.WindowExists(window.ClassName, window.Title + " (other)"));
            Assert.False(query.WindowExists(window.ClassName + ".other", null));
            Assert.False(query.WindowExists(null, window.Title[..^1]));
            Assert.False(query.WindowExists(window.ClassName, string.Empty)); // "" is not a wildcard, null is.
        });
    }

    [Fact]
    public void A_destroyed_window_is_no_longer_found()
    {
        var (className, title) = Sta.Run(() =>
        {
            using var window = CreateCaptioned();
            return (window.ClassName, window.Title);
        });

        Assert.False(new WindowQuery().WindowExists(className, title));
    }

    [Fact]
    public void Win32Text_reads_class_and_title_including_unicode_and_long_titles()
    {
        Sta.Run(() =>
        {
            var title = "Zażółć gęślą jaźń ✓ " + new string('x', 5000);
            using var window = TestWindow.Create(TestNative.WS_OVERLAPPEDWINDOW, 0, new Rectangle(0, 0, 200, 100), title);

            Assert.Equal(window.ClassName, Win32Text.GetClassName(window.Handle));
            var read = Win32Text.GetWindowTitle(window.Handle);
            Assert.StartsWith("Zażółć gęślą jaźń ✓ xxx", read, StringComparison.Ordinal);
            Assert.InRange(read.Length, 4000, 4096);
            Assert.Equal(string.Empty, Win32Text.GetClassName(IntPtr.Zero));
            Assert.Equal(string.Empty, Win32Text.GetWindowTitle(IntPtr.Zero));
        });
    }

    [Fact]
    public void Capture_and_the_foreground_checks_never_throw()
    {
        var inspector = new ForegroundWindowInspector();
        var snapshot = inspector.Capture();
        _output.WriteLine($"Foreground: exists={snapshot.Exists} class='{snapshot.ClassName}' explorer={snapshot.BelongsToExplorer} bounds={snapshot.WindowBounds} monitor={snapshot.MonitorBounds}");
        _ = inspector.IsFullScreenForeground();
        _ = inspector.IsTaskViewForeground();
        _ = new WindowQuery().GetForegroundWindow();
    }

    [SkippableFact]
    public void The_foreground_window_is_reported_by_class_and_title()
    {
        Sta.Run(() =>
        {
            using var window = CreateCaptioned();
            Skip.IfNot(window.TryBringToForeground(), NoForegroundReason);

            var info = new WindowQuery().GetForegroundWindow();
            Assert.Equal(new WindowInfo(window.ClassName, window.Title), info);

            var snapshot = new ForegroundWindowInspector().Capture();
            Assert.True(snapshot.Exists);
            Assert.True(snapshot.IsVisible);
            Assert.False(snapshot.IsMinimized);
            Assert.False(snapshot.IsShellWindow);
            Assert.False(snapshot.BelongsToExplorer);
            Assert.False(snapshot.HostsDesktopView);
            Assert.True(snapshot.HasCaption);
            Assert.False(snapshot.IsClickThrough);
            Assert.Equal(window.ClassName, snapshot.ClassName);
            Assert.Equal(new Rectangle(50, 50, 400, 300), snapshot.WindowBounds);
            Assert.False(snapshot.MonitorBounds.IsEmpty);
            Assert.False(new ForegroundWindowInspector().IsFullScreenForeground());
            Assert.False(new ForegroundWindowInspector().IsTaskViewForeground());
        });
    }

    [SkippableFact]
    public void A_borderless_window_covering_the_monitor_is_full_screen()
    {
        Sta.Run(() =>
        {
            var monitor = Screen.PrimaryScreen!.Bounds;
            using var window = TestWindow.Create(TestNative.WS_POPUP | TestNative.WS_VISIBLE, TestNative.WS_EX_TOPMOST, monitor);
            Skip.IfNot(window.TryBringToForeground(), NoForegroundReason);

            var snapshot = new ForegroundWindowInspector().Capture();
            _output.WriteLine($"window={snapshot.WindowBounds} monitor={snapshot.MonitorBounds} caption={snapshot.HasCaption}");

            Assert.False(snapshot.HasCaption);
            Assert.Equal(monitor, snapshot.MonitorBounds);
            Assert.True(FullScreenHeuristics.IsFullScreen(in snapshot));
            Assert.True(new ForegroundWindowInspector().IsFullScreenForeground());
        });
    }

    [SkippableFact]
    public void A_borderless_window_smaller_than_the_monitor_is_not_full_screen()
    {
        Sta.Run(() =>
        {
            var monitor = Screen.PrimaryScreen!.Bounds;
            var bounds = new Rectangle(monitor.X + 10, monitor.Y + 10, monitor.Width - 20, monitor.Height - 20);
            using var window = TestWindow.Create(TestNative.WS_POPUP | TestNative.WS_VISIBLE, TestNative.WS_EX_TOPMOST, bounds);
            Skip.IfNot(window.TryBringToForeground(), NoForegroundReason);

            Assert.False(new ForegroundWindowInspector().IsFullScreenForeground());
        });
    }
}
