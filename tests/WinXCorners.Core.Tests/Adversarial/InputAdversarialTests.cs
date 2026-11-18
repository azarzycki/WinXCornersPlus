using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Adversarial;

public class InputAdversarialTests
{
    private static readonly string[] KeyNames =
        ["a", "b", "control", "ctrl", "shift", "alt", "win", "lcontrol", "rcontrol", "lshift", "rmenu", "tab", "home", "f5", "escape", "lwin", "rwin", "menu", "rshift", "lalt"];

    // Invariants from the KeyEventPlanner contract, checked over random sequences with a fixed seed.
    [Fact]
    public void KeyEventPlanner_RandomSequencesKeepEveryInvariant()
    {
        var random = new Random(20261117);
        for (var round = 0; round < 5000; round++)
        {
            var tokens = Enumerable.Range(0, random.Next(1, 20)).Select(_ =>
            {
                var name = KeyNames[random.Next(KeyNames.Length)];
                return random.Next(3) switch { 0 => "_" + name, 1 => name + "_", _ => name };
            });
            var text = string.Join("+", tokens);
            Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);

            var userHeld = VirtualKeys.PhysicalModifierKeys.Where(_ => random.Next(3) == 0).ToList();
            var delay = random.Next(-1000, 1000);
            var events = KeyEventPlanner.Plan(sequence, userHeld.Concat(new ushort[] { 0x41, 0x11 }), delay);

            // 1. User modifiers are released first, in PhysicalModifierKeys order.
            var expectedPrefix = VirtualKeys.PhysicalModifierKeys.Where(userHeld.Contains).ToList();
            Assert.Equal(expectedPrefix, events.Take(expectedPrefix.Count).Select(e => e.KeyUp ? e.VirtualKey : (ushort)0).ToList());

            // 2. A user modifier is never pressed again unless the sequence itself names that exact key.
            var sequenceKeys = sequence.Steps.Select(s => s.VirtualKey).ToHashSet();
            Assert.DoesNotContain(events, e => !e.KeyUp && userHeld.Contains(e.VirtualKey) && !sequenceKeys.Contains(e.VirtualKey));

            // 3. Nothing is left logically down, and every key-down is followed later by a key-up of that key.
            var down = new HashSet<ushort>();
            for (var i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e.KeyUp)
                {
                    down.Remove(e.VirtualKey);
                }
                else
                {
                    down.Add(e.VirtualKey);
                    Assert.Contains(events.Skip(i + 1), later => later.KeyUp && later.VirtualKey == e.VirtualKey);
                }

                // 4. Delays within bounds; extended flag consistent.
                Assert.InRange(e.DelayAfterMs, 0, KeyEventPlanner.MaxDelayMs);
                Assert.Equal(VirtualKeys.IsExtended(e.VirtualKey), e.Extended);
            }

            Assert.Empty(down);
            Assert.Equal(0, events[^1].DelayAfterMs);
        }
    }

    [Fact]
    public void KeyEventPlanner_WorstCaseDurationIsBounded()
    {
        var text = string.Join("+", Enumerable.Repeat("a", HotkeySequence.MaxSteps));
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out _));
        var events = KeyEventPlanner.Plan(sequence, VirtualKeys.PhysicalModifierKeys, int.MaxValue);
        // 8 user releases + 64 presses (down+up): 136 events, about 34 s of injected input at the per-event cap.
        Assert.Equal(136, events.Count);
        Assert.Equal(135 * KeyEventPlanner.MaxDelayMs, KeyEventPlanner.TotalDurationMs(events));
    }

    // Microsoft's KEYEVENTF_EXTENDEDKEY documentation lists BREAK (VK_CANCEL, Ctrl+Pause) as an extended key.
    [Fact]
    public void VirtualKeys_CancelIsMarkedExtended()
    {
        Assert.True(VirtualKeys.TryGet("cancel", out var vk));
        Assert.Equal(0x03, vk);
        Assert.True(VirtualKeys.IsExtended(vk));
    }

    // Non-ASCII look-alikes must not parse as keys the README never names. U+017F upper-cases to 'S' under the
    // invariant culture, so "\u017F" is accepted as the S key.
    [Fact]
    public void VirtualKeys_LongSIsNotAcceptedAsS()
    {
        Assert.False(VirtualKeys.TryGet("\u017F", out _));
    }

    [Theory]
    [InlineData("\u0131")]
    [InlineData("\u017Fhift")]
    [InlineData("\u0131n\u017Fert")]
    [InlineData("\u212A")]
    [InlineData("\uFF21")]
    public void VirtualKeys_RejectsOtherNonAsciiLookAlikes(string name)
    {
        Assert.False(VirtualKeys.TryGet(name, out _));
    }
    [Theory]
    [InlineData(0x03, "CANCEL")]
    [InlineData(0x5F, "SLEEP")]
    [InlineData(0x6C, "SEPARATOR")]
    [InlineData(0xE2, "OEM_102")]
    [InlineData(0xFE, "OEM_CLEAR")]
    [InlineData(0xA5, "RMENU")]
    [InlineData(0x2C, "SNAPSHOT")]
    public void VirtualKeys_CodesMatchWinUserH(int code, string name)
    {
        Assert.True(VirtualKeys.TryGet(name, out var vk));
        Assert.Equal(code, vk);
    }

    [Fact]
    public void Parsers_NeverThrowOrHangOnRandomInput()
    {
        var random = new Random(42);
        const string alphabet = "#@[](),:?!+_ abcwinctrl‮\uD800\0";
        for (var i = 0; i < 20000; i++)
        {
            var text = new string(Enumerable.Range(0, random.Next(0, 60)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            HotkeySequence.TryParse(text, out _, out _);
            ConditionalHotkeyExpression.TryParse(text, out _, out _);
            var analysis = CustomCommandAnalyzer.Analyze(text);
            Assert.Equal(analysis.IsValid, analysis.Error is null);
        }

        Assert.False(HotkeySequence.TryParse(string.Concat(Enumerable.Repeat("a+", 1_000_000)), out _, out _));
        Assert.False(ConditionalHotkeyExpression.TryParse("#[" + new string(',', 1_000_000), out _, out _));
    }

    [Theory]
    [InlineData("#[a]:(b)?(c)", true)]
    [InlineData("#[a] @[,t] :( b ) ? ( c )", true)]
    [InlineData(":(b)?(c)", false)]
    [InlineData("#[]:(b)", false)]
    [InlineData("#[a]:(b)?(c)?(d)", false)]
    [InlineData("#[a]:(b+nosuchkey)?(c)", false)]
    [InlineData("#[a]:(b)?(nosuchkey)", false)]
    [InlineData("#[a,t?(x)]:(b)", true)]
    [InlineData("#[a]:(b)x", false)]
    [InlineData("#[a]:((b))", false)]
    public void Conditional_Grammar(string text, bool valid)
    {
        Assert.Equal(valid, ConditionalHotkeyExpression.TryParse(text, out var expression, out var error));
        if (valid)
        {
            Assert.NotEmpty(expression!.Conditions);
        }
        else
        {
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
