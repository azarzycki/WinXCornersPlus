using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;

namespace WinXCorners.App.UiTests;

/// <summary>The monitor driven by fake input facts and a fake clock; the "executor" is a recorder.</summary>
public sealed class HotCornerMonitorTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    private static readonly Point Centre = new(960, 540);
    private static readonly Point TopLeft = new(0, 0);

    [Fact]
    public void TriggersExactlyOncePerVisit()
    {
        var rig = new Rig();
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        Assert.Equal([ActionIds.FileExplorer], rig.Triggered);

        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        Assert.Equal(2, rig.Triggered.Count);
    }

    [Fact]
    public void NeverTriggersWhileTheFlyoutIsOpen()
    {
        var rig = new Rig { Suppressed = true };
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        Assert.Empty(rig.Triggered);
    }

    [Fact]
    public void ADelayedCornerCountsDown_ThenTriggersOnce_AndHidesTheBadge()
    {
        var rig = new Rig();
        rig.Settings.TopLeftDelayEnabled = true;
        rig.Settings.TopLeftDelayIndex = 1; // 0.5 s
        rig.Settings.ShowCountdown = true;

        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 30);
        Assert.Single(rig.Triggered);
        Assert.True(rig.Countdowns > 0, "The countdown badge was never requested.");
        Assert.True(rig.Hides > 0, "The countdown badge was never hidden.");
    }

    [Fact]
    public void ACornerSetToNone_RunsNothing()
    {
        var rig = new Rig();
        rig.Settings.TopLeftActionId = ActionIds.None;
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        Assert.Empty(rig.Triggered);
    }

    /// <summary>
    /// Review V2 D2: GetCursorPos fails on the lock screen and the UAC secure desktop. The old code read that as (0,0),
    /// the top-left corner, and ran its action once per lock. A failed read must never trigger anything.
    /// </summary>
    [Fact]
    public void A_pointer_that_cannot_be_read_never_triggers()
    {
        var rig = new Rig();
        rig.Visit(Centre, 3);
        rig.Unavailable(40);
        rig.Unavailable(40);
        Assert.Empty(rig.Triggered);
        Assert.Contains(rig.Log.Snapshot(), static l => l.Contains("Hot corners paused", StringComparison.Ordinal));
    }

    [Fact]
    public void Another_desktop_having_the_input_never_triggers()
    {
        var rig = new Rig { InputDesktopIsOurs = false };
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 40);
        Assert.Empty(rig.Triggered);
    }

    [Fact]
    public void Lock_then_unlock_with_the_pointer_parked_in_a_corner_fires_only_after_it_leaves_and_returns()
    {
        var rig = new Rig();
        rig.Visit(Centre, 3);
        rig.Monitor.OnSessionChange(SessionChange.SessionLock);
        rig.Visit(TopLeft, 40);
        Assert.Empty(rig.Triggered);

        rig.Monitor.OnSessionChange(SessionChange.SessionUnlock);
        rig.Visit(TopLeft, 40);
        Assert.Empty(rig.Triggered);
        Assert.Contains(rig.Log.Snapshot(), static l => l.Contains("Hot corners resumed", StringComparison.Ordinal));

        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 20);
        Assert.Equal([ActionIds.FileExplorer], rig.Triggered);
    }

    [Fact]
    public void A_countdown_in_progress_is_hidden_when_the_session_locks()
    {
        var rig = new Rig();
        rig.Settings.TopLeftDelayEnabled = true;
        rig.Settings.TopLeftDelayIndex = 3;
        rig.Settings.ShowCountdown = true;
        rig.Visit(Centre, 3);
        rig.Visit(TopLeft, 5);
        Assert.True(rig.Countdowns > 0);

        rig.Monitor.OnSessionChange(SessionChange.SessionLock);
        Assert.True(rig.Hides > 0, "The countdown badge stayed up over the lock screen.");
        rig.Visit(TopLeft, 200);
        Assert.Empty(rig.Triggered);
    }

    private sealed class Rig
    {
        private Point? _cursor = Centre;
        private long _clock = 1_000;

        internal Rig()
        {
            Settings = new ApplicationSettings { IgnoreFullScreen = false };
            Monitor = new HotCornerMonitor(
                () => Settings,
                () => _cursor,
                static _ => Screen,
                static () => false,
                static _ => false,
                () => _clock,
                () => Suppressed,
                static () => false,
                Log,
                () => InputDesktopIsOurs);
            var _monitor = Monitor;
            _monitor.ActionTriggered += Triggered.Add;
            _monitor.CountdownRequested += (_, _, _) => Countdowns++;
            _monitor.CountdownHidden += () => Hides++;
        }

        internal ApplicationSettings Settings { get; }

        internal HotCornerMonitor Monitor { get; }

        internal BoundedLog Log { get; } = new();

        internal bool InputDesktopIsOurs { get; set; } = true;

        internal bool Suppressed { get; set; }

        internal List<string> Triggered { get; } = [];

        internal int Countdowns { get; private set; }

        internal int Hides { get; private set; }

        /// <summary>Puts the pointer at <paramref name="point"/> for <paramref name="ticks"/> 50 ms timer ticks.</summary>
        internal void Visit(Point point, int ticks)
        {
            _cursor = point;
            Tick(ticks);
        }

        /// <summary>GetCursorPos fails for <paramref name="ticks"/> ticks (lock screen, UAC prompt).</summary>
        internal void Unavailable(int ticks)
        {
            _cursor = null;
            Tick(ticks);
        }

        private void Tick(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                Monitor.Evaluate();
                _clock += 50;
            }
        }
    }
}

public sealed class CountdownOverlayTests
{
    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void ShowsTheRemainingTimeInTheCorner_WithoutTakingActivation(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        Ui.Run(() =>
        {
            using var overlay = new CountdownOverlay();
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            overlay.ShowCountdown(bounds, HotCornerArea.TopLeft, TimeSpan.FromSeconds(2.4));
            Ui.Pump(100);
            Assert.True(overlay.Visible);
            Assert.Equal("3", overlay.CountdownText);
            Assert.Equal(new Point(bounds.Left + 8, bounds.Top + 8), overlay.Location);
            Assert.NotSame(overlay, Form.ActiveForm);
            Screenshot.Capture(overlay, $"countdown-{theme.ToString().ToLowerInvariant()}");

            overlay.ShowCountdown(bounds, HotCornerArea.BottomRight, TimeSpan.FromSeconds(0.4));
            Ui.Pump(50);
            Assert.Equal(0.4.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), overlay.CountdownText);
            Assert.Equal(new Point(bounds.Right - overlay.Width - 8, bounds.Bottom - overlay.Height - 8), overlay.Location);
            overlay.HideCountdown();
            Assert.False(overlay.Visible);
        });
    }
}
