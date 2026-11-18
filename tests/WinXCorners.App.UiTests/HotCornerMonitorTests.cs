using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Diagnostics;
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

    private sealed class Rig
    {
        private readonly HotCornerMonitor _monitor;
        private Point _cursor = Centre;
        private long _clock = 1_000;

        internal Rig()
        {
            Settings = new ApplicationSettings { IgnoreFullScreen = false };
            _monitor = new HotCornerMonitor(
                () => Settings,
                () => _cursor,
                static _ => Screen,
                static () => false,
                static _ => false,
                () => _clock,
                () => Suppressed,
                static () => false,
                new BoundedLog());
            _monitor.ActionTriggered += Triggered.Add;
            _monitor.CountdownRequested += (_, _, _) => Countdowns++;
            _monitor.CountdownHidden += () => Hides++;
        }

        internal ApplicationSettings Settings { get; }

        internal bool Suppressed { get; set; }

        internal List<string> Triggered { get; } = [];

        internal int Countdowns { get; private set; }

        internal int Hides { get; private set; }

        /// <summary>Puts the pointer at <paramref name="point"/> for <paramref name="ticks"/> 50 ms timer ticks.</summary>
        internal void Visit(Point point, int ticks)
        {
            _cursor = point;
            for (var i = 0; i < ticks; i++)
            {
                _monitor.Evaluate();
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
