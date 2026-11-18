using System.Drawing;
using WinXCorners.Core.HotCorners;

namespace WinXCorners.Core.Tests.HotCorners;

public class FullScreenHeuristicsTests
{
    private static readonly Rectangle Monitor = new(0, 0, 1920, 1080);

    private static readonly ForegroundWindowSnapshot Game = new(
        Exists: true,
        IsShellWindow: false,
        IsVisible: true,
        IsMinimized: false,
        ClassName: "UnityWndClass",
        BelongsToExplorer: false,
        HostsDesktopView: false,
        HasCaption: false,
        IsClickThrough: false,
        WindowBounds: Monitor,
        MonitorBounds: Monitor);

    [Fact]
    public void CaptionlessWindowCoveringTheMonitorIsFullScreen()
    {
        Assert.True(FullScreenHeuristics.IsFullScreen(Game));
    }

    public static TheoryData<string, ForegroundWindowSnapshot> Disqualified()
    {
        return new TheoryData<string, ForegroundWindowSnapshot>
        {
            { "missing", Game with { Exists = false } },
            { "shell window", Game with { IsShellWindow = true } },
            { "hidden", Game with { IsVisible = false } },
            { "minimized", Game with { IsMinimized = true } },
            { "desktop class", Game with { ClassName = "Progman" } },
            { "desktop class (case)", Game with { ClassName = "workerw" } },
            { "taskbar", Game with { ClassName = "Shell_TrayWnd" } },
            { "secondary taskbar", Game with { ClassName = "SHELL_SECONDARYTRAYWND" } },
            { "task view", Game with { ClassName = "XamlExplorerHostIslandWindow" } },
            { "core window", Game with { ClassName = "windows.ui.core.corewindow" } },
            { "explorer", Game with { BelongsToExplorer = true } },
            { "desktop view", Game with { HostsDesktopView = true } },
            { "caption (maximised app)", Game with { HasCaption = true } },
            { "click-through overlay", Game with { IsClickThrough = true } },
            { "one pixel short", Game with { WindowBounds = new Rectangle(0, 0, 1920, 1079) } },
            { "same size but shifted", Game with { WindowBounds = new Rectangle(1, 0, 1920, 1080) } },
            { "on another monitor", Game with { WindowBounds = new Rectangle(1920, 0, 1920, 1080) } },
            { "empty monitor", Game with { MonitorBounds = Rectangle.Empty, WindowBounds = Rectangle.Empty } },
            { "degenerate monitor", Game with { MonitorBounds = new Rectangle(0, 0, 0, 1080) } }
        };
    }

    [Theory]
    [MemberData(nameof(Disqualified))]
    public void DisqualifiedWindowsAreNotFullScreen(string reason, ForegroundWindowSnapshot snapshot)
    {
        Assert.False(FullScreenHeuristics.IsFullScreen(snapshot), reason);
    }

    [Fact]
    public void WindowLargerThanTheMonitorCounts()
    {
        Assert.True(FullScreenHeuristics.IsFullScreen(Game with { WindowBounds = new Rectangle(-8, -8, 1936, 1096) }));
    }

    [Fact]
    public void NegativeOriginMonitorWorks()
    {
        var secondary = new Rectangle(-2560, -200, 2560, 1440);
        Assert.True(FullScreenHeuristics.IsFullScreen(Game with { WindowBounds = secondary, MonitorBounds = secondary }));
    }

    [Fact]
    public void NullOrEmptyClassNameIsAllowed()
    {
        Assert.True(FullScreenHeuristics.IsFullScreen(Game with { ClassName = null! }));
        Assert.True(FullScreenHeuristics.IsFullScreen(Game with { ClassName = string.Empty }));
    }

    [Theory]
    [InlineData("XamlExplorerHostIslandWindow", true)]
    [InlineData("xamlexplorerhostislandwindow", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("Progman", false)]
    [InlineData("Windows.UI.Core.CoreWindowX", false)]
    [InlineData(" Windows.UI.Core.CoreWindow", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsTaskViewClass(string? className, bool expected)
    {
        Assert.Equal(expected, FullScreenHeuristics.IsTaskViewClass(className));
    }

    [Theory]
    [InlineData("Progman", true)]
    [InlineData("WORKERW", true)]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Shell_SecondaryTrayWnd", true)]
    [InlineData("XamlExplorerHostIslandWindow", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("Notepad", false)]
    [InlineData("Shell_TrayWnd2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsShellClass(string? className, bool expected)
    {
        Assert.Equal(expected, FullScreenHeuristics.IsShellClass(className));
    }

    [Fact]
    public void EveryTaskViewClassIsAlsoAShellClass()
    {
        // This is why the original's "all-windows over Task View" exemption could never apply.
        foreach (var name in new[] { "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow" })
        {
            Assert.True(FullScreenHeuristics.IsTaskViewClass(name));
            Assert.True(FullScreenHeuristics.IsShellClass(name));
            Assert.False(FullScreenHeuristics.IsFullScreen(Game with { ClassName = name }));
        }
    }
}
