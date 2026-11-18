using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Input;

public sealed class KeyEventPlannerTests
{
    private const ushort Tab = 0x09;
    private const ushort Shift = 0x10;
    private const ushort Control = 0x11;
    private const ushort Alt = 0x12;
    private const ushort Escape = 0x1B;
    private const ushort Home = 0x24;
    private const ushort Left = 0x25;
    private const ushort Up = 0x26;
    private const ushort Right = 0x27;
    private const ushort Down = 0x28;
    private const ushort LWin = 0x5B;
    private const ushort RWin = 0x5C;
    private const ushort LShift = 0xA0;
    private const ushort LControl = 0xA2;
    private const ushort RControl = 0xA3;
    private const ushort RMenu = 0xA5;

    private static readonly ushort[] NoModifiers = [];

    private static HotkeySequence Parse(string text)
    {
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);
        return sequence;
    }

    // (key, up, extended) without delays, to compare event order and flags.
    private static (ushort Key, bool Up, bool Extended)[] Shape(IEnumerable<KeyEvent> events) =>
        events.Select(static e => (e.VirtualKey, e.KeyUp, e.Extended)).ToArray();

    private static (ushort, bool, bool) D(ushort key, bool extended = false) => (key, false, extended);

    private static (ushort, bool, bool) U(ushort key, bool extended = false) => (key, true, extended);

    public static TheoryData<string, (ushort, bool, bool)[]> ExpectedPlans => new()
    {
        {
            "_control+k+control_+_control+_b",
            [D(Control), D('K'), U('K'), U(Control), D(Control), D('B'), U('B'), U(Control)]
        },
        { "_win+tab", [D(LWin, true), D(Tab), U(Tab), U(LWin, true)] },
        { "escape", [D(Escape), U(Escape)] },
        { "_win+d", [D(LWin, true), D('D'), U('D'), U(LWin, true)] },
        { "_win+a", [D(LWin, true), D('A'), U('A'), U(LWin, true)] },
        { "_win+n", [D(LWin, true), D('N'), U('N'), U(LWin, true)] },
        { "_win+home", [D(LWin, true), D(Home, true), U(Home, true), U(LWin, true)] },
        { "_control+_alt+tab", [D(Control), D(Alt), D(Tab), U(Tab), U(Alt), U(Control)] },
        { "win", [D(LWin, true), U(LWin, true)] },
    };

    [Theory]
    [MemberData(nameof(ExpectedPlans))]
    public void Plan_ReadmeAndBuiltInSequences_ProduceExactEvents(string text, (ushort, bool, bool)[] expected)
    {
        Assert.Equal(expected, Shape(KeyEventPlanner.Plan(Parse(text), NoModifiers)));
    }

    [Fact]
    public void Plan_UserHoldingCtrlToTrigger_ReleasesCtrlFirstAndNeverPressesItAgain()
    {
        var events = KeyEventPlanner.Plan(Parse("_win+tab"), [LControl]);

        Assert.Equal([U(LControl), D(LWin, true), D(Tab), U(Tab), U(LWin, true)], Shape(events));
        Assert.DoesNotContain(events, e => e.VirtualKey == LControl && !e.KeyUp);
    }

    [Fact]
    public void Plan_SeveralHeldModifiers_ReleasedInFixedOrderRegardlessOfInputOrder()
    {
        var a = KeyEventPlanner.Plan(Parse("a"), [RWin, RControl, LShift]);
        var b = KeyEventPlanner.Plan(Parse("a"), [LShift, RWin, RControl]);

        Assert.Equal([U(LShift), U(RControl, true), U(RWin, true), D('A'), U('A')], Shape(a));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Plan_UnknownAndDuplicateModifierEntries_AreIgnored()
    {
        // 0x11 (generic CONTROL), 'A' and 0xFFFF are not physical modifier keys; LShift appears twice.
        var events = KeyEventPlanner.Plan(Parse("a"), [LShift, Control, 'A', 0xFFFF, LShift]);

        Assert.Equal([U(LShift), D('A'), U('A')], Shape(events));
    }

    [Fact]
    public void Plan_HeldKeysNeverReleased_AreReleasedAtEndInReverseOrder()
    {
        var events = KeyEventPlanner.Plan(Parse("_control+_shift+_alt+x"), NoModifiers);

        Assert.Equal([D(Control), D(Shift), D(Alt), D('X'), U('X'), U(Alt), U(Shift), U(Control)], Shape(events));
    }

    [Fact]
    public void Plan_ReleasingHeldKeyMidway_IsNotReleasedAgainAtEnd()
    {
        var events = KeyEventPlanner.Plan(Parse("_control+_shift+control_+x"), NoModifiers);

        Assert.Equal([D(Control), D(Shift), U(Control), D('X'), U('X'), U(Shift)], Shape(events));
    }

    [Fact]
    public void Plan_ReleasingKeyThatWasNotHeld_SendsKeyUp()
    {
        Assert.Equal([U(Control)], Shape(KeyEventPlanner.Plan(Parse("control_"), NoModifiers)));
    }

    [Fact]
    public void Plan_HoldingSameKeyTwice_ReleasesItOnce()
    {
        var events = KeyEventPlanner.Plan(Parse("_shift+a+_shift+b"), NoModifiers);

        Assert.Equal([D(Shift), D('A'), U('A'), D(Shift), D('B'), U('B'), U(Shift)], Shape(events));
    }

    [Fact]
    public void Plan_PressingAHeldKey_LeavesItUpWithoutExtraRelease()
    {
        var events = KeyEventPlanner.Plan(Parse("_shift+shift"), NoModifiers);

        Assert.Equal([D(Shift), D(Shift), U(Shift)], Shape(events));
    }

    [Fact]
    public void Plan_ExtendedFlag_FollowsTheKey()
    {
        var events = KeyEventPlanner.Plan(Parse("home+left+up+right+down+rwin+a+shift+rctrl+ralt"), NoModifiers);

        var downs = events.Where(static e => !e.KeyUp).ToDictionary(static e => e.VirtualKey, static e => e.Extended);
        Assert.True(downs[Home]);
        Assert.True(downs[Left]);
        Assert.True(downs[Up]);
        Assert.True(downs[Right]);
        Assert.True(downs[Down]);
        Assert.True(downs[RWin]);
        Assert.True(downs[RControl]);
        Assert.True(downs[RMenu]);
        Assert.False(downs['A']);
        Assert.False(downs[Shift]);
        Assert.All(events, e => Assert.Equal(VirtualKeys.IsExtended(e.VirtualKey), e.Extended));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(250, 250)]
    [InlineData(251, 250)]
    [InlineData(int.MaxValue, 250)]
    [InlineData(int.MinValue, 0)]
    public void Plan_Delay_IsClampedAndLastEventHasNone(int requested, int expected)
    {
        var events = KeyEventPlanner.Plan(Parse("_control+k"), [LControl], requested);

        Assert.Equal(5, events.Count);
        Assert.All(events.Take(events.Count - 1), e => Assert.Equal(expected, e.DelayAfterMs));
        Assert.Equal(0, events[^1].DelayAfterMs);
        Assert.Equal(expected * 4, KeyEventPlanner.TotalDurationMs(events));
    }

    [Fact]
    public void Plan_DefaultDelay_IsTenMilliseconds()
    {
        var events = KeyEventPlanner.Plan(Parse("a+b"), NoModifiers);

        Assert.Equal([10, 10, 10, 0], events.Select(static e => e.DelayAfterMs));
        Assert.Equal(30, KeyEventPlanner.TotalDurationMs(events));
    }

    [Fact]
    public void Plan_SingleReleaseEvent_HasZeroDelay()
    {
        var events = KeyEventPlanner.Plan(Parse("a_"), NoModifiers, 100);

        Assert.Equal(0, Assert.Single(events).DelayAfterMs);
    }

    [Fact]
    public void Plan_Result_IsReadOnly()
    {
        var events = KeyEventPlanner.Plan(Parse("a"), NoModifiers);

        Assert.Throws<NotSupportedException>(() => ((IList<KeyEvent>)events).Add(default));
    }

    [Fact]
    public void Plan_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => KeyEventPlanner.Plan(null!, NoModifiers));
        Assert.Throws<ArgumentNullException>(() => KeyEventPlanner.Plan(Parse("a"), null!));
        Assert.Throws<ArgumentNullException>(() => KeyEventPlanner.TotalDurationMs(null!));
    }

    [Fact]
    public void TotalDurationMs_EmptyList_IsZero()
    {
        Assert.Equal(0, KeyEventPlanner.TotalDurationMs([]));
    }

    [Fact]
    public void Plan_RandomValidSequences_NeverLeaveAnyKeyDown()
    {
        string[] tokens = ["a", "_a", "a_", "shift", "_shift", "shift_", "_control", "control_", "_win", "home", "_lcontrol", "lcontrol_"];
        var random = new Random(4242);
        for (var i = 0; i < 2000; i++)
        {
            var count = random.Next(1, HotkeySequence.MaxSteps + 1);
            var text = string.Join('+', Enumerable.Range(0, count).Select(_ => tokens[random.Next(tokens.Length)]));
            var userModifiers = VirtualKeys.PhysicalModifierKeys.Where(_ => random.Next(4) == 0).ToArray();

            var events = KeyEventPlanner.Plan(Parse(text), userModifiers, random.Next(-10, 300));

            var down = new HashSet<ushort>(userModifiers);
            foreach (var e in events)
            {
                if (e.KeyUp)
                {
                    down.Remove(e.VirtualKey);
                }
                else
                {
                    down.Add(e.VirtualKey);
                }
            }

            Assert.Empty(down);
            Assert.Equal(0, events[^1].DelayAfterMs);
            Assert.All(events, e => Assert.InRange(e.DelayAfterMs, 0, KeyEventPlanner.MaxDelayMs));
            Assert.InRange(KeyEventPlanner.TotalDurationMs(events), 0, KeyEventPlanner.MaxDelayMs * (events.Count - 1));
        }
    }
}
