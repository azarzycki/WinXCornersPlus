using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Input;

public sealed class ConditionalHotkeyExpressionTests
{
    private sealed class FakeWindowQuery : IWindowQuery
    {
        public WindowInfo? Foreground { get; init; }

        public HashSet<(string? Class, string? Title)> Existing { get; init; } = [];

        public int ForegroundCalls { get; private set; }

        public List<(string? Class, string? Title)> ExistsCalls { get; } = [];

        public WindowInfo? GetForegroundWindow()
        {
            ForegroundCalls++;
            return Foreground;
        }

        public bool WindowExists(string? className, string? title)
        {
            ExistsCalls.Add((className, title));
            return Existing.Contains((className, title));
        }
    }

    private static ConditionalHotkeyExpression Parse(string text)
    {
        Assert.True(ConditionalHotkeyExpression.TryParse(text, out var expression, out var error), error);
        Assert.Null(error);
        return expression;
    }

    private static string ParseError(string? text)
    {
        Assert.False(ConditionalHotkeyExpression.TryParse(text, out var expression, out var error));
        Assert.Null(expression);
        Assert.False(string.IsNullOrWhiteSpace(error));
        return error;
    }

    private static string Plan(HotkeySequence? sequence) =>
        sequence is null
            ? "<none>"
            : string.Join(' ', KeyEventPlanner.Plan(sequence, []).Select(static e => $"{(e.KeyUp ? "U" : "D")}{e.VirtualKey:X2}"));

    [Fact]
    public void TryParse_ReadmeVsCodeExample_ParsesBothBranchesToExactEvents()
    {
        var expression = Parse("#[Chrome_WidgetWin_1]:(_control+k+control_+_control+_b)?(win)");

        Assert.Equal([new WindowCondition(WindowConditionScope.Foreground, "Chrome_WidgetWin_1", "")], expression.Conditions);
        Assert.Equal("D11 D4B U4B U11 D11 D42 U42 U11", Plan(expression.WhenMatched));
        Assert.Equal("D5B U5B", Plan(expression.Otherwise));
    }

    [Fact]
    public void TryParse_ReadmeAltTabExample_ParsesBothBranchesToExactEvents()
    {
        var expression = Parse("#[MultitaskingViewFrame]:(escape)?(_control+_alt+tab)");

        Assert.Equal([new WindowCondition(WindowConditionScope.Foreground, "MultitaskingViewFrame", "")], expression.Conditions);
        Assert.Equal("D1B U1B", Plan(expression.WhenMatched));
        Assert.Equal("D11 D12 D09 U09 U12 U11", Plan(expression.Otherwise));
    }

    [Fact]
    public void TryParse_WithoutElseBranch_HasNullOtherwise()
    {
        Assert.Null(Parse("#[Notepad]:(f5)").Otherwise);
    }

    [Fact]
    public void TryParse_MultipleConditionsMixingScopes_KeepsOrderAndTrimsParts()
    {
        var expression = Parse("  #[Notepad, Untitled ] @[,Calculator]\t#[CabinetWClass] : ( f5 ) ? ( escape )  ");

        Assert.Equal(
            [
                new WindowCondition(WindowConditionScope.Foreground, "Notepad", "Untitled"),
                new WindowCondition(WindowConditionScope.AnyWindow, "", "Calculator"),
                new WindowCondition(WindowConditionScope.Foreground, "CabinetWClass", ""),
            ],
            expression.Conditions);
        Assert.NotNull(expression.Otherwise);
    }

    [Fact]
    public void TryParse_TitleRunsToFirstClosingBracket()
    {
        Assert.Equal("a [b", Parse("#[cls,a [b]:(x)").Conditions[0].TitleText);
        Assert.Contains("Expected ':'", ParseError("#[cls,[Running] x]:(x)"));
    }

    [Fact]
    public void TryParse_QuestionParenInsideTitle_IsTitleTextNotAnElseBranch()
    {
        // The old regex found "?(win)" anywhere in the text and treated it as the else branch (review B9).
        var expression = Parse("#[cls,Save?(win)]:(escape)");

        Assert.Equal("Save?(win)", expression.Conditions[0].TitleText);
        Assert.Null(expression.Otherwise);
    }

    [Fact]
    public void TryParse_TitleWithCommas_KeepsEverythingAfterFirstComma()
    {
        Assert.Equal("a, b, c", Parse("@[cls,a, b, c]:(x)").Conditions[0].TitleText);
    }

    [Theory]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData(":(win)", "Expected a window condition")]
    [InlineData(":(win)?(escape)", "Expected a window condition")]
    [InlineData("(win)", "Expected a window condition")]
    [InlineData("Notepad:(win)", "Expected a window condition")]
    [InlineData("#Notepad]:(win)", "Expected '[' directly after '#'")]
    [InlineData("# [Notepad]:(win)", "Expected '[' directly after '#'")]
    [InlineData("#", "Expected '[' directly after '#'")]
    [InlineData("@", "Expected '[' directly after '@'")]
    [InlineData("#[Notepad:(win)", "Missing ']'")]
    [InlineData("#[", "Missing ']'")]
    [InlineData("#[]:(win)", "needs a window class, a title, or both")]
    [InlineData("#[ , ]:(win)", "needs a window class, a title, or both")]
    [InlineData("@[,]:(win)", "needs a window class, a title, or both")]
    [InlineData("#[Notepad]", "Expected ':'")]
    [InlineData("#[Notepad](win)", "Expected ':'")]
    [InlineData("#[Notepad] x :(win)", "Expected ':'")]
    [InlineData("#[a],#[b]:(win)", "Expected ':'")]
    [InlineData("#[Notepad]:", "Expected '(' after ':' at the end")]
    [InlineData("#[Notepad]:win", "Expected '(' after ':'")]
    [InlineData("#[Notepad]:(win", "Missing ')'")]
    [InlineData("#[Notepad]:(win)?(escape", "Missing ')'")]
    [InlineData("#[Notepad]:(win)?", "Expected '(' after '?' at the end")]
    [InlineData("#[Notepad]:(win)?escape", "Expected '(' after '?'")]
    [InlineData("#[Notepad]:(win(x))", "Nested '('")]
    [InlineData("#[Notepad]:((win))", "Nested '('")]
    [InlineData("#[Notepad]:(win)?((escape))", "Nested '('")]
    [InlineData("#[Notepad]:(win) trailing", "Unexpected text")]
    [InlineData("#[Notepad]:(win)?(escape)x", "Unexpected text")]
    [InlineData("#[Notepad]:(win)?(escape)?(tab)", "Unexpected text")]
    [InlineData("#[Notepad]:(win))", "Unexpected text")]
    [InlineData("#[Notepad]:(win):(tab)", "Unexpected text")]
    [InlineData("#[Notepad]:()", "In the hotkeys after ':'")]
    [InlineData("#[Notepad]:(  )", "In the hotkeys after ':'")]
    [InlineData("#[Notepad]:(win)?()", "In the hotkeys after '?'")]
    [InlineData("#[Notepad]:(_a_)", "both held")]
    [InlineData("#[Notepad]:(win)?(lbutton)", "mouse button")]
    public void TryParse_MalformedExpressions_AreRejectedWithSpecificError(string? text, string expectedFragment)
    {
        Assert.Contains(expectedFragment, ParseError(text));
    }

    [Fact]
    public void TryParse_TypoInElseBranch_RejectsWholeExpressionWithContext()
    {
        var error = ParseError("#[Notepad]:(win)?(_control+_alt+tabb)");

        Assert.Equal(
            "In the hotkeys after '?' (starting at column 19): Key 3 'tabb' (column 15): unknown key name 'tabb'.",
            error);
    }

    [Fact]
    public void TryParse_ErrorColumns_PointAtTheProblem()
    {
        Assert.Contains("column 12 ('x :(win)')", ParseError("#[Notepad] x :(win)"));
        Assert.Contains("for the '(' at column 12", ParseError("#[Notepad]:(win"));
        Assert.Contains("condition starting at column 3", ParseError("  #[abc"));
    }

    [Fact]
    public void Match_ForegroundClass_IgnoresCase()
    {
        var expression = Parse("#[chrome_widgetwin_1]:(a)?(b)");
        var query = new FakeWindowQuery { Foreground = new WindowInfo("Chrome_WidgetWin_1", "x") };

        Assert.True(expression.IsMatch(query));
        Assert.Same(expression.WhenMatched, expression.Select(query));
    }

    [Fact]
    public void Match_ForegroundDifferentClass_SelectsOtherwise()
    {
        var expression = Parse("#[Notepad]:(a)?(b)");
        var query = new FakeWindowQuery { Foreground = new WindowInfo("Chrome_WidgetWin_1", "Notepad") };

        Assert.False(expression.IsMatch(query));
        Assert.Same(expression.Otherwise, expression.Select(query));
        Assert.Empty(query.ExistsCalls);
    }

    [Fact]
    public void Match_ForegroundNoMatchAndNoElse_SelectsNothing()
    {
        var expression = Parse("#[Notepad]:(a)");

        Assert.Null(expression.Select(new FakeWindowQuery { Foreground = new WindowInfo("Other", "") }));
    }

    [Theory]
    [InlineData("Visual Studio Code", true)]
    [InlineData("main.cs - visual studio code", true)]
    [InlineData("Visual Studio", false)]
    [InlineData("", false)]
    public void Match_ForegroundTitle_IsCaseInsensitiveContains(string title, bool expected)
    {
        var expression = Parse("#[Chrome_WidgetWin_1,Visual Studio Code]:(a)");

        Assert.Equal(expected, expression.IsMatch(new FakeWindowQuery { Foreground = new WindowInfo("Chrome_WidgetWin_1", title) }));
    }

    [Fact]
    public void Match_ForegroundEmptyClass_MatchesAnyClassByTitle()
    {
        var expression = Parse("#[,Calculator]:(a)");

        Assert.True(expression.IsMatch(new FakeWindowQuery { Foreground = new WindowInfo("ApplicationFrameWindow", "Calculator") }));
        Assert.False(expression.IsMatch(new FakeWindowQuery { Foreground = new WindowInfo("ApplicationFrameWindow", "Settings") }));
    }

    [Fact]
    public void Match_NoForegroundWindow_DoesNotMatch()
    {
        var expression = Parse("#[,x]:(a)?(b)");
        var query = new FakeWindowQuery { Foreground = null };

        Assert.False(expression.IsMatch(query));
        Assert.Same(expression.Otherwise, expression.Select(query));
    }

    [Fact]
    public void Match_DefaultWindowInfoWithNullStrings_DoesNotThrow()
    {
        Assert.False(Parse("#[cls,title]:(a)").IsMatch(new FakeWindowQuery { Foreground = default(WindowInfo) }));
    }

    [Fact]
    public void Match_AnyWindowClassOnly_PassesNullTitle()
    {
        var query = new FakeWindowQuery { Existing = [("Notepad", null)] };

        Assert.True(Parse("@[Notepad]:(a)").IsMatch(query));
        Assert.Equal([("Notepad", (string?)null)], query.ExistsCalls);
        Assert.Equal(0, query.ForegroundCalls);
    }

    [Fact]
    public void Match_AnyWindowTitleOnly_PassesNullClassNotEmptyString()
    {
        var query = new FakeWindowQuery { Existing = [(null, "Calculator")] };

        Assert.True(Parse("@[,Calculator]:(a)").IsMatch(query));
        Assert.Equal([((string?)null, "Calculator")], query.ExistsCalls);
    }

    [Fact]
    public void Match_AnyWindowClassAndTitle_PassesBoth()
    {
        var query = new FakeWindowQuery();

        Assert.False(Parse("@[ Notepad , Untitled - Notepad ]:(a)").IsMatch(query));
        Assert.Equal([("Notepad", "Untitled - Notepad")], query.ExistsCalls);
    }

    [Fact]
    public void Match_MultipleConditions_AreOredAndShortCircuit()
    {
        var expression = Parse("#[Notepad] @[,A] @[,B] @[,C]:(a)?(b)");
        var query = new FakeWindowQuery { Foreground = new WindowInfo("Other", ""), Existing = [(null, "B")] };

        Assert.True(expression.IsMatch(query));
        Assert.Equal([((string?)null, "A"), (null, "B")], query.ExistsCalls);
    }

    [Fact]
    public void Match_SeveralForegroundConditions_ReadForegroundOnce()
    {
        var expression = Parse("#[a]#[b]#[c]:(x)");
        var query = new FakeWindowQuery { Foreground = new WindowInfo("c", "") };

        Assert.True(expression.IsMatch(query));
        Assert.Equal(1, query.ForegroundCalls);
    }

    [Fact]
    public void Match_NullQuery_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Parse("#[a]:(x)").IsMatch(null!));
    }

    [Fact]
    public void TryParse_RandomInput_NeverThrowsAndReturnsExactlyOneOfValueOrError()
    {
        string[] pieces = ["#", "@", "[", "]", ",", ":", "(", ")", "?", " ", "Notepad", "win", "_control+k", "+", "_", "x"];
        var random = new Random(1117);
        for (var i = 0; i < 10000; i++)
        {
            var count = random.Next(0, 16);
            var text = string.Concat(Enumerable.Range(0, count).Select(_ => pieces[random.Next(pieces.Length)]));

            var ok = ConditionalHotkeyExpression.TryParse(text, out var expression, out var error);

            Assert.Equal(ok, expression is not null);
            Assert.Equal(ok, error is null);
            if (ok)
            {
                Assert.NotEmpty(expression!.Conditions);
                Assert.All(expression.Conditions, c => Assert.True(c.ClassName.Length + c.TitleText.Length > 0));
                _ = expression.Select(new FakeWindowQuery { Foreground = new WindowInfo("Notepad", "win") });
            }
        }
    }
}
