using System.Drawing;
using WinXCorners.Core.Actions;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.HotCorners;

public class HotCornerEngineTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1080);
    private static readonly Rectangle Secondary = new(-2560, -200, 2560, 1440);

    private static readonly Point TopLeft = new(0, 0);
    private static readonly Point TopRight = new(1919, 0);
    private static readonly Point BottomLeft = new(0, 1079);
    private static readonly Point BottomRight = new(1919, 1079);
    private static readonly Point Center = new(960, 540);

    [Flags]
    public enum F
    {
        None = 0,
        Button = 1,
        NoModifier = 2,
        Suppressed = 4
    }

    private const HotCornerOutcome Idle = HotCornerOutcome.Idle;
    private const HotCornerOutcome Counting = HotCornerOutcome.Counting;
    private const HotCornerOutcome Triggered = HotCornerOutcome.Triggered;
    private const HotCornerOutcome Blocked = HotCornerOutcome.BlockedByFullScreen;

    /// <summary>
    /// Drives an engine through samples and records every decision. Unless <paramref name="armed"/> is false, the
    /// engine first sees one sample in the middle of the screen: corners fire on entry only, so that is where the
    /// pointer "comes from" in every scenario below.
    /// </summary>
    private sealed class Timeline(ApplicationSettings settings, HotCornerEngine? engine = null, bool armed = true)
    {
        public HotCornerEngine Engine { get; } = Arm(engine ?? new HotCornerEngine(), settings, armed);

        private static HotCornerEngine Arm(HotCornerEngine engine, ApplicationSettings settings, bool armed)
        {
            if (armed)
            {
                Assert.Equal(HotCornerDecision.Idle, engine.Evaluate(settings, new HotCornerInput(Center, Primary, 0, false, true, false)));
            }

            return engine;
        }

        public Rectangle Screen { get; set; } = Primary;

        public HotCornerDecision At(long t, Point p, F flags = F.None) => At(t, p.X, p.Y, flags);

        public HotCornerDecision At(long t, int x, int y, F flags = F.None)
        {
            var input = new HotCornerInput(
                new Point(x, y),
                Screen,
                t,
                AnyMouseButtonDown: flags.HasFlag(F.Button),
                ModifierSatisfied: !flags.HasFlag(F.NoModifier),
                Suppressed: flags.HasFlag(F.Suppressed));
            return Engine.Evaluate(settings, input);
        }

        /// <summary>Feeds (t, point, flags, expected outcome) rows and asserts each outcome.</summary>
        public void Expect(params (long T, Point P, F Flags, HotCornerOutcome Outcome)[] rows)
        {
            foreach (var row in rows)
            {
                var decision = At(row.T, row.P, row.Flags);
                Assert.True(row.Outcome == decision.Outcome, $"t={row.T} at {row.P} ({row.Flags}): expected {row.Outcome}, got {decision.Outcome}");
            }
        }
    }

    private static ApplicationSettings Settings(Action<ApplicationSettings>? configure = null)
    {
        var s = new ApplicationSettings
        {
            TopLeftActionId = ActionIds.FileExplorer,
            TopRightActionId = ActionIds.TaskManager,
            BottomLeftActionId = ActionIds.Desktop,
            BottomRightActionId = ActionIds.LockScreen,
            IgnoreFullScreen = true
        };
        configure?.Invoke(s);
        return s;
    }

    private static ApplicationSettings WithDelay(int index, Action<ApplicationSettings>? configure = null)
    {
        return Settings(s =>
        {
            s.GlobalDelayEnabled = true;
            s.GlobalDelayIndex = index;
            configure?.Invoke(s);
        });
    }

    [Fact]
    public void ZeroDelay_TriggersOnFirstEvaluation()
    {
        var timeline = new Timeline(Settings());
        var decision = timeline.At(1000, BottomRight);

        Assert.Equal(new HotCornerDecision(Triggered, HotCornerArea.BottomRight, ActionIds.LockScreen, TimeSpan.Zero, false), decision);
    }

    [Fact]
    public void Delay_CountsDownAndFiresExactlyOnce()
    {
        var timeline = new Timeline(WithDelay(1, s => s.ShowCountdown = true)); // 500 ms

        var first = timeline.At(1000, TopLeft);
        Assert.Equal(new HotCornerDecision(Counting, HotCornerArea.TopLeft, ActionIds.FileExplorer, TimeSpan.FromMilliseconds(500), true), first);

        var middle = timeline.At(1300, TopLeft);
        Assert.Equal(Counting, middle.Outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(200), middle.Remaining);

        Assert.Equal(Counting, timeline.At(1499, TopLeft).Outcome);
        Assert.Equal(Triggered, timeline.At(1500, TopLeft).Outcome);

        // Parked in the corner: never again.
        timeline.Expect((1600, TopLeft, F.None, Idle), (5000, TopLeft, F.None, Idle), (60_000, TopLeft, F.None, Idle));
    }

    [Fact]
    public void Delay_LateSampleStillFiresOnce()
    {
        var timeline = new Timeline(WithDelay(3));
        timeline.Expect((0, TopLeft, F.None, Counting), (9_999, TopLeft, F.None, Triggered), (10_000, TopLeft, F.None, Idle));
    }

    [Fact]
    public void ShowCountdown_FalseWhenSettingOff()
    {
        var timeline = new Timeline(WithDelay(3));
        var decision = timeline.At(0, TopLeft);
        Assert.Equal(Counting, decision.Outcome);
        Assert.False(decision.ShowCountdown);
    }

    [Fact]
    public void Rearm_OnlyAfterLeavingTheReleaseZone()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect(
            (0, BottomRight, F.None, Triggered),
            (10, new Point(1900, 1060), F.None, Idle),     // moved a bit, still inside 32 px
            (20, BottomRight, F.None, Idle),               // back into the corner: latched
            (30, new Point(1888, 1048), F.None, Idle),     // innermost pixel of the zone
            (40, BottomRight, F.None, Idle),
            (50, new Point(1887, 1079), F.None, Idle),     // left the zone horizontally: re-armed
            (60, BottomRight, F.None, Triggered));
    }

    [Fact]
    public void Rearm_LeavingVerticallyAlsoCounts()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect(
            (0, TopLeft, F.None, Triggered),
            (10, new Point(0, 32), F.None, Idle),
            (20, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void Rearm_ExitSampleThatLandsInAnotherCornerIsEvaluatedImmediately()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect((0, TopLeft, F.None, Triggered), (10, TopRight, F.None, Triggered));
    }

    [Fact]
    public void Latch_SurvivesScreenBoundsChange()
    {
        var timeline = new Timeline(Settings());
        Assert.Equal(Triggered, timeline.At(0, BottomRight).Outcome);

        // A resolution change that keeps the pointer within the zone measured on the old bounds stays latched...
        timeline.Screen = new Rectangle(0, 0, 1900, 1070);
        Assert.Equal(Idle, timeline.At(10, 1899, 1069).Outcome);
        timeline.Screen = Primary;
        Assert.Equal(Idle, timeline.At(20, 1899, 1069).Outcome);

        // ...whereas after a drop to 1280x720, (1279, 719) is the new bottom-right corner but far outside the
        // old release zone, so it re-arms there.
        timeline.Screen = new Rectangle(0, 0, 1280, 720);
        Assert.Equal(Triggered, timeline.At(30, 1279, 719).Outcome);
    }

    /// <summary>
    /// The release zone ends at the edge of the screen the corner fired on. Once the pointer has crossed onto the
    /// neighbouring monitor, that monitor's adjacent corner works straight away instead of staying blocked.
    /// </summary>
    [Fact]
    public void Latch_ReleasesWhenThePointerMovesOntoTheNeighbouringMonitor()
    {
        var timeline = new Timeline(Settings());
        Assert.Equal(Triggered, timeline.At(0, TopLeft).Outcome);

        // The pointer slides onto the left secondary monitor's right edge, then up into its top-right corner.
        timeline.Screen = Secondary;
        Assert.Equal(Idle, timeline.At(10, -1, 0).Outcome);
        var decision = timeline.At(20, -1, -200);
        Assert.Equal((Triggered, HotCornerArea.TopRight), (decision.Outcome, decision.Area));

        // Back on the primary, its top-left corner fires again on entry.
        timeline.Screen = Primary;
        Assert.Equal(Idle, timeline.At(30, Center).Outcome);
        Assert.Equal(Triggered, timeline.At(40, TopLeft).Outcome);
    }

    [Fact]
    public void Drag_NeverFires()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect(
            (0, TopLeft, F.Button, Idle),
            (1000, TopLeft, F.Button, Idle),
            (5000, TopLeft, F.Button, Idle));

        // Releasing the button in the corner counts as arriving there.
        Assert.Equal(Triggered, timeline.At(6000, TopLeft).Outcome);
    }

    [Fact]
    public void Drag_InterruptsTheDwell()
    {
        var timeline = new Timeline(WithDelay(3));
        timeline.Expect(
            (0, TopLeft, F.None, Counting),
            (900, TopLeft, F.Button, Idle),
            (1000, TopLeft, F.None, Counting),   // restarted at 1000
            (1999, TopLeft, F.None, Counting),
            (2000, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void Modifier_PressedLateStartsTheDwellThen()
    {
        var timeline = new Timeline(WithDelay(1));
        timeline.Expect(
            (0, TopLeft, F.NoModifier, Idle),
            (2000, TopLeft, F.NoModifier, Idle),
            (3000, TopLeft, F.None, Counting),
            (3499, TopLeft, F.None, Counting),
            (3500, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void Modifier_ReleasedMidDwellClearsTheTimer()
    {
        var timeline = new Timeline(WithDelay(1));
        timeline.Expect(
            (0, TopLeft, F.None, Counting),
            (400, TopLeft, F.NoModifier, Idle),
            (450, TopLeft, F.None, Counting),
            (900, TopLeft, F.None, Counting),
            (950, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void Modifier_ZeroDelayFiresTheMomentItIsPressed()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect((0, TopLeft, F.NoModifier, Idle), (10, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void SwitchingCorners_RestartsTheDwell()
    {
        var timeline = new Timeline(WithDelay(1));
        var a = timeline.At(0, TopLeft);
        var b = timeline.At(400, TopRight);
        Assert.Equal((Counting, HotCornerArea.TopLeft), (a.Outcome, a.Area));
        Assert.Equal((Counting, HotCornerArea.TopRight, ActionIds.TaskManager), (b.Outcome, b.Area, b.ActionId));
        Assert.Equal(TimeSpan.FromMilliseconds(500), b.Remaining);
        timeline.Expect((899, TopRight, F.None, Counting), (900, TopRight, F.None, Triggered));
    }

    [Fact]
    public void LeavingTheCornerClearsTheDwell()
    {
        var timeline = new Timeline(WithDelay(1));
        timeline.Expect(
            (0, TopLeft, F.None, Counting),
            (400, Center, F.None, Idle),
            (450, TopLeft, F.None, Counting),
            (949, TopLeft, F.None, Counting),
            (950, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void Disabled_IsIdle()
    {
        var timeline = new Timeline(Settings(s => s.HotCornersEnabled = false));
        foreach (var corner in new[] { TopLeft, TopRight, BottomLeft, BottomRight })
        {
            Assert.Equal(HotCornerDecision.Idle, timeline.At(0, corner));
        }
    }

    [Fact]
    public void Suppressed_IsIdleAndClearsTheDwell()
    {
        var timeline = new Timeline(WithDelay(1));
        timeline.Expect(
            (0, TopLeft, F.None, Counting),
            (100, TopLeft, F.Suppressed, Idle),
            (2000, TopLeft, F.Suppressed, Idle),
            (2100, TopLeft, F.None, Counting),
            (2600, TopLeft, F.None, Triggered));
    }

    [Theory]
    [InlineData(ActionIds.None)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("format-c")]
    [InlineData("File-Explorer")]
    [InlineData("custom-5")]
    [InlineData("custom-0")]
    public void NoOpAndUnknownActionIds_AreIdle(string actionId)
    {
        var timeline = new Timeline(Settings(s => s.TopLeftActionId = actionId));
        Assert.Equal(HotCornerDecision.Idle, timeline.At(0, TopLeft));
        Assert.Equal(HotCornerDecision.Idle, timeline.At(5000, TopLeft));
    }

    [Fact]
    public void NullActionId_IsIdle()
    {
        var timeline = new Timeline(Settings(s => s.TopLeftActionId = null!));
        Assert.Equal(HotCornerDecision.Idle, timeline.At(0, TopLeft));
    }

    [Fact]
    public void CustomAction_WithCustomCommandsDisabled_IsIdle()
    {
        var settings = WithDelay(1, s =>
        {
            s.TopLeftActionId = ActionCatalog.CustomId(0);
            s.CustomCommands[0].Command = "notepad.exe";
        });
        var timeline = new Timeline(settings);
        timeline.Expect((0, TopLeft, F.None, Idle), (600, TopLeft, F.None, Idle));

        settings.EnableCustomCommands = true;
        var counting = timeline.At(700, TopLeft);
        Assert.Equal((Counting, "custom-1"), (counting.Outcome, counting.ActionId));
        Assert.Equal(Triggered, timeline.At(1200, TopLeft).Outcome);
    }

    [Fact]
    public void FullScreen_BlocksAndIsNotConsultedOutsideCornersOrForNoOps()
    {
        var calls = new List<string>();
        var engine = new HotCornerEngine(id => { calls.Add(id); return true; });
        var timeline = new Timeline(Settings(s => s.TopRightActionId = ActionIds.None), engine);

        // Plain mouse movement: never consulted.
        for (var x = 0; x < 1900; x += 7)
        {
            Assert.Equal(Idle, timeline.At(x, x + 5, 500).Outcome);
        }

        // No-op corner, dragging, modifier missing, suppressed: never consulted.
        timeline.Expect(
            (2000, TopRight, F.None, Idle),
            (2001, TopLeft, F.Button, Idle),
            (2002, TopLeft, F.NoModifier, Idle),
            (2003, TopLeft, F.Suppressed, Idle));
        Assert.Empty(calls);

        var blocked = timeline.At(2004, TopLeft);
        Assert.Equal(new HotCornerDecision(Blocked, HotCornerArea.TopLeft, ActionIds.FileExplorer, TimeSpan.Zero, false), blocked);
        Assert.Equal([ActionIds.FileExplorer], calls);

        // Parked in the blocked corner: stays blocked without re-running the predicate.
        timeline.Expect((2050, TopLeft, F.None, Blocked), (3000, TopLeft, F.None, Blocked));
        Assert.Single(calls);

        // Leaving and coming back re-checks once.
        timeline.Expect((3100, Center, F.None, Idle), (3200, TopLeft, F.None, Blocked));
        Assert.Equal(2, calls.Count);
    }

    [Fact]
    public void FullScreen_IsConsultedAtDwellStartAndJustBeforeFiringOnly()
    {
        var blocked = false;
        var calls = 0;
        var engine = new HotCornerEngine(_ => { calls++; return blocked; });
        var timeline = new Timeline(WithDelay(3), engine);

        timeline.Expect((0, TopLeft, F.None, Counting));
        Assert.Equal(1, calls);
        for (var t = 50; t < 1000; t += 50)
        {
            Assert.Equal(Counting, timeline.At(t, TopLeft).Outcome);
        }

        Assert.Equal(1, calls);

        // A full-screen app came to the foreground during the delay.
        blocked = true;
        Assert.Equal(Blocked, timeline.At(1000, TopLeft).Outcome);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void FullScreen_NotBlockingLetsTheActionFire()
    {
        var calls = 0;
        var engine = new HotCornerEngine(_ => { calls++; return false; });
        var timeline = new Timeline(Settings(), engine);
        Assert.Equal(Triggered, timeline.At(0, TopLeft).Outcome);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FullScreen_PredicateIgnoredWhenIgnoreFullScreenIsOff()
    {
        var calls = 0;
        var engine = new HotCornerEngine(_ => { calls++; return true; });
        var timeline = new Timeline(Settings(s => s.IgnoreFullScreen = false), engine);
        Assert.Equal(Triggered, timeline.At(0, TopLeft).Outcome);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FullScreen_NotConsultedWhileLatched()
    {
        var calls = 0;
        var engine = new HotCornerEngine(_ => { calls++; return false; });
        var timeline = new Timeline(Settings(), engine);
        timeline.Expect((0, TopLeft, F.None, Triggered), (10, TopLeft, F.None, Idle), (20, TopLeft, F.None, Idle));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GlobalDelay_OverridesPerCornerDelay()
    {
        var timeline = new Timeline(WithDelay(0, s => { s.TopLeftDelayEnabled = true; s.TopLeftDelayIndex = 5; }));
        timeline.Expect((0, TopLeft, F.None, Counting), (249, TopLeft, F.None, Counting), (250, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void PerCornerDelay_AppliesOnlyToItsCorner()
    {
        var timeline = new Timeline(Settings(s => { s.TopLeftDelayEnabled = true; s.TopLeftDelayIndex = 5; }));
        timeline.Expect(
            (0, TopRight, F.None, Triggered),
            (100, TopLeft, F.None, Counting),
            (1599, TopLeft, F.None, Counting),
            (1600, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void TimestampGoingBackwards_RestartsTheDwell()
    {
        var timeline = new Timeline(WithDelay(1));
        timeline.Expect(
            (10_000, TopLeft, F.None, Counting),
            (10_400, TopLeft, F.None, Counting),
            (100, TopLeft, F.None, Counting),     // counter anomaly: restart at 100, never fire early
            (599, TopLeft, F.None, Counting),
            (600, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void MultiMonitor_NegativeOriginCornersWork()
    {
        var timeline = new Timeline(Settings()) { Screen = Secondary };
        var cases = new (Point P, HotCornerArea Area)[]
        {
            (new Point(-2560, -200), HotCornerArea.TopLeft),
            (new Point(-1, -200), HotCornerArea.TopRight),
            (new Point(-2560, 1239), HotCornerArea.BottomLeft),
            (new Point(-1, 1239), HotCornerArea.BottomRight)
        };

        var t = 0L;
        foreach (var (p, area) in cases)
        {
            var decision = timeline.At(t += 10, p);
            Assert.Equal((Triggered, area), (decision.Outcome, decision.Area));
            Assert.Equal(Idle, timeline.At(t += 10, new Point(-1280, 520)).Outcome);
        }

        // Points next to (but not in) the corners of the negative-origin screen.
        Assert.Equal(Idle, timeline.At(t += 10, new Point(-2559, -200)).Outcome);
        Assert.Equal(Idle, timeline.At(t + 10, new Point(-1, -199)).Outcome);
    }

    [Fact]
    public void Reset_ClearsDwellAndLatchAndDisarmsUntilThePointerLeaves()
    {
        var delayed = WithDelay(1);
        var engine = new HotCornerEngine();
        var timeline = new Timeline(delayed, engine);
        timeline.Expect((0, TopLeft, F.None, Counting));
        engine.Reset();

        // Still parked in the corner: Reset disarms, so the dwell does not start again until the pointer has left.
        timeline.Expect((400, TopLeft, F.None, Idle), (5000, TopLeft, F.None, Idle));
        timeline.Expect((5010, Center, F.None, Idle), (5020, TopLeft, F.None, Counting), (5519, TopLeft, F.None, Counting),
            (5520, TopLeft, F.None, Triggered), (5530, TopLeft, F.None, Idle));

        // Reset also forgets the latch: leaving the 1-pixel corner (but not the release zone) is enough afterwards.
        engine.Reset();
        timeline.Expect((5540, TopLeft, F.None, Idle), (5550, new Point(5, 5), F.None, Idle), (5560, TopLeft, F.None, Counting));
    }

    /// <summary>
    /// The app calls Reset whenever settings change. With Lock Screen on a corner, unlocking with the pointer still
    /// there must not lock again.
    /// </summary>
    [Fact]
    public void Reset_WhileParkedInACornerThatJustFired_NeverFiresItAgainWithoutLeaving()
    {
        var timeline = new Timeline(Settings());
        timeline.Expect((0, BottomRight, F.None, Triggered), (10, BottomRight, F.None, Idle));
        timeline.Engine.Reset();
        timeline.Expect((20, BottomRight, F.None, Idle), (30, BottomRight, F.None, Idle), (40, BottomRight, F.None, Idle));
        timeline.Expect((50, Center, F.None, Idle), (60, BottomRight, F.None, Triggered));
    }

    [Fact]
    public void NewEngine_PointerAlreadyRestingInACorner_DoesNotFireUntilItLeavesAndReturns()
    {
        var timeline = new Timeline(Settings(), armed: false);
        timeline.Expect((0, TopLeft, F.None, Idle), (60_000, TopLeft, F.None, Idle));
        timeline.Expect((60_010, Center, F.None, Idle), (60_020, TopLeft, F.None, Triggered));
    }

    /// <summary>Any sample outside the corners arms, even while dragging, with the modifier up or the flyout open.</summary>
    [Theory]
    [InlineData(F.Button)]
    [InlineData(F.NoModifier)]
    [InlineData(F.Suppressed)]
    public void NewEngine_IsArmedByAnySampleOutsideTheCorners(F flags)
    {
        var timeline = new Timeline(Settings(), armed: false);
        timeline.Expect((0, BottomLeft, F.None, Idle), (10, Center, flags, Idle), (20, BottomLeft, F.None, Triggered));
    }

    /// <summary>Entering a corner with the modifier up is an entry: pressing the modifier afterwards fires.</summary>
    [Fact]
    public void NewEngine_EnteringACornerBeforeTheModifierIsHeld_StillArmsIt()
    {
        var timeline = new Timeline(Settings(s => s.HotCornerModifierKey = HotCornerModifierKey.Ctrl), armed: false);
        timeline.Expect((0, Center, F.NoModifier, Idle), (10, TopRight, F.NoModifier, Idle), (20, TopRight, F.None, Triggered));
    }

    [Fact]
    public void Reset_ClearsTheFullScreenBlock()
    {
        var blocked = true;
        var engine = new HotCornerEngine(_ => blocked);
        var timeline = new Timeline(Settings(), engine);
        timeline.Expect((0, TopLeft, F.None, Blocked));
        blocked = false;
        timeline.Expect((10, TopLeft, F.None, Blocked));
        engine.Reset();
        timeline.Expect((20, TopLeft, F.None, Idle), (30, Center, F.None, Idle), (40, TopLeft, F.None, Triggered));
    }

    /// <summary>
    /// An enabled custom slot that would do nothing (no command, or one that does not parse) never counts down:
    /// a countdown that ends in nothing would only mislead.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("!nosuchkey")]
    [InlineData("#[")]
    [InlineData("calc\u0001.exe")]
    public void CustomAction_EmptyOrInvalidCommand_IsIdle(string? command)
    {
        var settings = WithDelay(1, s =>
        {
            s.TopLeftActionId = ActionCatalog.CustomId(0);
            s.EnableCustomCommands = true;
            s.ShowCountdown = true;
            s.CustomCommands[0].Command = command!;
        });
        var timeline = new Timeline(settings);
        timeline.Expect((0, TopLeft, F.None, Idle), (600, TopLeft, F.None, Idle));

        settings.CustomCommands[0].Command = "!win+d";
        timeline.Expect((700, TopLeft, F.None, Counting), (1200, TopLeft, F.None, Triggered));
    }

    [Fact]
    public void CustomAction_MissingSlot_IsIdle()
    {
        var settings = Settings(s =>
        {
            s.TopLeftActionId = ActionCatalog.CustomId(3);
            s.EnableCustomCommands = true;
            s.CustomCommands = [new CustomCommandSettings { Command = "notepad.exe" }];
        });
        var timeline = new Timeline(settings);
        timeline.Expect((0, TopLeft, F.None, Idle));
        settings.CustomCommands = null!;
        timeline.Expect((10, TopLeft, F.None, Idle));
        settings.CustomCommands = [null!, null!, null!, null!];
        timeline.Expect((20, TopLeft, F.None, Idle));
    }

    [Fact]
    public void Evaluate_NullSettingsThrows()
    {
        var engine = new HotCornerEngine();
        var input = new HotCornerInput(TopLeft, Primary, 0, false, true, false);
        Assert.Throws<ArgumentNullException>(() => engine.Evaluate(null!, input));
    }

    [Fact]
    public void Evaluate_EmptyScreenBoundsIsIdle()
    {
        var timeline = new Timeline(Settings()) { Screen = Rectangle.Empty };
        Assert.Equal(HotCornerDecision.Idle, timeline.At(0, 0, 0));
    }

    [Fact]
    public void Idle_DecisionHasNoAreaOrAction()
    {
        Assert.Equal(new HotCornerDecision(Idle, HotCornerArea.None, string.Empty, TimeSpan.Zero, false), HotCornerDecision.Idle);
    }
}
