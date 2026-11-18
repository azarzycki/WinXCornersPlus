using System.Drawing;
using WinXCorners.Core.Actions;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.HotCorners;

/// <summary>
/// Review V2 D2: a failed pointer read used to look like (0,0), the top-left pixel, so the top-left action fired once
/// behind the lock screen or a UAC prompt. The engine now gets an explicit "pointer available" fact.
/// </summary>
public class HotCornerEnginePointerTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    private static readonly Point TopLeft = new(0, 0);
    private static readonly Point Center = new(960, 540);

    private static ApplicationSettings Settings(int delayIndex = -1)
    {
        var settings = new ApplicationSettings
        {
            TopLeftActionId = ActionIds.FileExplorer,
            TopRightActionId = ActionIds.TaskManager,
            BottomLeftActionId = ActionIds.Desktop,
            BottomRightActionId = ActionIds.LockScreen
        };
        if (delayIndex >= 0)
        {
            settings.GlobalDelayEnabled = true;
            settings.GlobalDelayIndex = delayIndex;
        }

        return settings;
    }

    private static HotCornerInput Sample(long t, Point p, bool available = true) =>
        new(p, Screen, t, AnyMouseButtonDown: false, ModifierSatisfied: true, Suppressed: false, PointerAvailable: available);

    [Fact]
    public void PointerAvailable_defaults_to_true()
    {
        Assert.True(new HotCornerInput(Center, Screen, 0, false, true, false).PointerAvailable);
    }

    [Fact]
    public void An_unavailable_pointer_never_fires_whatever_position_it_reports()
    {
        var engine = new HotCornerEngine();
        var settings = Settings();
        Assert.Equal(HotCornerOutcome.Idle, engine.Evaluate(settings, Sample(0, Center)).Outcome); // armed

        for (var t = 1; t <= 200; t++)
        {
            // What the old Cursor.Position gave when GetCursorPos failed: (0,0), the top-left corner.
            Assert.Equal(HotCornerDecision.Idle, engine.Evaluate(settings, Sample(t, TopLeft, available: false)));
        }
    }

    [Fact]
    public void An_unavailable_pointer_cancels_a_countdown_in_progress()
    {
        var engine = new HotCornerEngine();
        var settings = Settings(delayIndex: 1);
        engine.Evaluate(settings, Sample(0, Center));
        Assert.Equal(HotCornerOutcome.Counting, engine.Evaluate(settings, Sample(10, TopLeft)).Outcome);

        Assert.Equal(HotCornerDecision.Idle, engine.Evaluate(settings, Sample(20, TopLeft, available: false)));

        // Back, still parked in the corner, long after the delay: nothing fires until the pointer leaves first.
        Assert.Equal(HotCornerOutcome.Idle, engine.Evaluate(settings, Sample(60_000, TopLeft)).Outcome);
        Assert.Equal(HotCornerOutcome.Idle, engine.Evaluate(settings, Sample(120_000, TopLeft)).Outcome);
    }

    [Fact]
    public void Lock_then_unlock_with_the_pointer_parked_in_a_corner_does_not_fire_until_it_leaves_and_returns()
    {
        var engine = new HotCornerEngine();
        var settings = Settings();
        var session = new SessionActivity();
        HotCornerDecision Tick(long t, Point p) =>
            engine.Evaluate(settings, Sample(t, p, session.IsPointerAvailable(positionRead: true, inputDesktopIsOurs: true)));

        Assert.Equal(HotCornerOutcome.Idle, Tick(0, Center).Outcome);
        session.Apply(SessionChange.SessionLock);
        for (var t = 1; t < 50; t++)
        {
            Assert.Equal(HotCornerOutcome.Idle, Tick(t, TopLeft).Outcome);
        }

        session.Apply(SessionChange.SessionUnlock);
        Assert.Equal(HotCornerOutcome.Idle, Tick(100, TopLeft).Outcome);
        Assert.Equal(HotCornerOutcome.Idle, Tick(200, TopLeft).Outcome);

        Assert.Equal(HotCornerOutcome.Idle, Tick(300, Center).Outcome);
        Assert.Equal(HotCornerOutcome.Triggered, Tick(400, TopLeft).Outcome);
    }

    [Fact]
    public void A_secure_desktop_while_the_pointer_was_latched_in_a_corner_does_not_re_fire_it()
    {
        var engine = new HotCornerEngine();
        var settings = Settings();
        engine.Evaluate(settings, Sample(0, Center));
        Assert.Equal(HotCornerOutcome.Triggered, engine.Evaluate(settings, Sample(1, TopLeft)).Outcome);

        // A UAC prompt appears (GetCursorPos fails) and goes away with the pointer still in the corner.
        Assert.Equal(HotCornerDecision.Idle, engine.Evaluate(settings, Sample(2, TopLeft, available: false)));
        Assert.Equal(HotCornerOutcome.Idle, engine.Evaluate(settings, Sample(3, TopLeft)).Outcome);
    }
}
