using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Input;

public sealed class HotkeySequenceTests
{
    private const ushort Control = 0x11;
    private const ushort Alt = 0x12;

    private static HotkeySequence Parse(string text)
    {
        Assert.True(HotkeySequence.TryParse(text, out var sequence, out var error), error);
        Assert.Null(error);
        return sequence;
    }

    private static string ParseError(string? text)
    {
        Assert.False(HotkeySequence.TryParse(text, out var sequence, out var error));
        Assert.Null(sequence);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error;
    }

    [Fact]
    public void TryParse_ReadmeVsCodeExample_ProducesHoldPressReleaseSteps()
    {
        var sequence = Parse("_control+k+control_+_control+_b");

        Assert.Equal(
            new[]
            {
                new KeyStep(Control, KeyStepKind.Hold),
                new KeyStep('K', KeyStepKind.Press),
                new KeyStep(Control, KeyStepKind.Release),
                new KeyStep(Control, KeyStepKind.Hold),
                new KeyStep('B', KeyStepKind.Hold),
            },
            sequence.Steps);
    }

    [Theory]
    [InlineData("_win+tab")]
    [InlineData("escape")]
    [InlineData("_win+d")]
    [InlineData("_win+a")]
    [InlineData("_win+n")]
    [InlineData("_win+home")]
    [InlineData("_control+_alt+tab")]
    public void TryParse_BuiltInSequences_AreValid(string text)
    {
        Assert.NotEmpty(Parse(text).Steps);
    }

    [Theory]
    [InlineData("_CONTROL+_Alt+TaB")]
    [InlineData("_control+_alt+tab")]
    [InlineData("  _control + _alt +tab  ")]
    [InlineData("\t_control\t+\n_alt+ tab\r\n")]
    [InlineData("_ctrl+_menu+tab")]
    public void TryParse_CaseWhitespaceAndAliasVariants_AreEquivalent(string text)
    {
        Assert.Equal(
            new[] { new KeyStep(Control, KeyStepKind.Hold), new KeyStep(Alt, KeyStepKind.Hold), new KeyStep(0x09, KeyStepKind.Press) },
            Parse(text).Steps);
    }

    [Theory]
    [InlineData("++a")]
    [InlineData("a++")]
    [InlineData("+ + a + +")]
    public void TryParse_EmptyTokensBetweenPlusSigns_AreIgnored(string text)
    {
        Assert.Equal(new[] { new KeyStep('A', KeyStepKind.Press) }, Parse(text).Steps);
    }

    [Fact]
    public void TryParse_ReleaseOfKeyThatIsNotHeld_IsLegal()
    {
        Assert.Equal(new[] { new KeyStep(Control, KeyStepKind.Release) }, Parse("control_").Steps);
    }

    [Fact]
    public void TryParse_NamesWithInnerUnderscores_StillSupportHoldAndRelease()
    {
        Assert.Equal(
            new[] { new KeyStep(0xA6, KeyStepKind.Hold), new KeyStep(0xA6, KeyStepKind.Release), new KeyStep(0xA6, KeyStepKind.Press) },
            Parse("_browser_back+browser_back_+browser_back").Steps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void TryParse_Blank_IsError(string? text)
    {
        Assert.Contains("empty", ParseError(text));
    }

    [Theory]
    [InlineData("+")]
    [InlineData("+++")]
    [InlineData(" + ")]
    public void TryParse_OnlySeparators_IsError(string text)
    {
        Assert.Contains("no keys", ParseError(text));
    }

    [Fact]
    public void TryParse_TypoInLastToken_ProducesNoSequenceAndNamesTokenAndColumn()
    {
        var error = ParseError("_control+k+control_+_control+_bb");

        Assert.Contains("Key 5", error);
        Assert.Contains("'_bb'", error);
        Assert.Contains("column 30", error);
        Assert.Contains("unknown key name 'bb'", error);
    }

    [Fact]
    public void TryParse_ColumnSkipsLeadingWhitespaceAndEmptyTokens()
    {
        var error = ParseError("a ++  nope");

        Assert.Contains("Key 2 'nope' (column 7)", error);
    }

    [Theory]
    [InlineData("_a_")]
    [InlineData("_control_")]
    [InlineData("__")]
    public void TryParse_HoldAndReleaseInOneToken_IsError(string text)
    {
        Assert.Contains("both held", ParseError(text));
    }

    [Theory]
    [InlineData("_")]
    [InlineData("a+_")]
    public void TryParse_LoneUnderscore_IsError(string text)
    {
        Assert.Contains("must be attached", ParseError(text));
    }

    [Theory]
    [InlineData("__a")]
    [InlineData("a__")]
    [InlineData("control-k")]
    [InlineData("ctrl k")]
    public void TryParse_MalformedNames_AreUnknownKeys(string text)
    {
        Assert.Contains("unknown key name", ParseError(text));
    }

    [Theory]
    [InlineData("LBUTTON")]
    [InlineData("_rbutton")]
    [InlineData("mbutton_")]
    [InlineData("a+XBUTTON1")]
    [InlineData("XBUTTON2")]
    public void TryParse_MouseButtons_AreRejectedWithExplanation(string text)
    {
        Assert.Contains("is a mouse button", ParseError(text));
    }

    [Fact]
    public void TryParse_ExactlyMaxSteps_IsAccepted()
    {
        var text = string.Join('+', Enumerable.Repeat("a", HotkeySequence.MaxSteps));

        Assert.Equal(HotkeySequence.MaxSteps, Parse(text).Steps.Count);
    }

    [Fact]
    public void TryParse_MoreThanMaxSteps_IsError()
    {
        var text = string.Join('+', Enumerable.Repeat("a", HotkeySequence.MaxSteps + 1));

        Assert.Contains($"more than {HotkeySequence.MaxSteps}", ParseError(text));
    }

    [Fact]
    public void TryParse_EmptyTokensDoNotCountTowardsMaxSteps()
    {
        var text = string.Join("++", Enumerable.Repeat("a", HotkeySequence.MaxSteps));

        Assert.Equal(HotkeySequence.MaxSteps, Parse(text).Steps.Count);
    }

    [Fact]
    public void TryParse_HugeUnknownToken_ErrorEchoIsShortened()
    {
        var error = ParseError(new string('q', 10_000));

        Assert.True(error.Length < 200, error);
        Assert.Contains("...", error);
    }

    [Fact]
    public void TryParse_RandomInput_NeverThrowsAndReturnsExactlyOneOfValueOrError()
    {
        const string alphabet = "_+ \tabcdefkxyzABC019!#@[](),:?-";
        string[] words = ["control", "_win", "tab_", "f12", "lbutton", "_a_", "+", "_", " ", "browser_back"];
        var random = new Random(20261117);
        for (var i = 0; i < 5000; i++)
        {
            var builder = new System.Text.StringBuilder();
            var length = random.Next(0, 40);
            for (var j = 0; j < length; j++)
            {
                builder.Append(random.Next(3) == 0 ? words[random.Next(words.Length)] : alphabet[random.Next(alphabet.Length)].ToString());
            }

            var text = builder.ToString();
            var ok = HotkeySequence.TryParse(text, out var sequence, out var error);
            Assert.Equal(ok, sequence is not null);
            Assert.Equal(ok, error is null);
            if (ok)
            {
                Assert.InRange(sequence!.Steps.Count, 1, HotkeySequence.MaxSteps);
            }
        }
    }
}
