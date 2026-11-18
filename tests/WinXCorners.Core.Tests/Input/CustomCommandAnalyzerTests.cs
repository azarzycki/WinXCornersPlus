using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Input;

public sealed class CustomCommandAnalyzerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Analyze_Blank_IsValidEmpty(string? command)
    {
        Assert.Equal(
            new CustomCommandAnalysis(CustomCommandKind.Empty, true, null, null, null, null),
            CustomCommandAnalyzer.Analyze(command));
    }

    [Fact]
    public void Analyze_BangPrefix_IsHotkeySequence()
    {
        var analysis = CustomCommandAnalyzer.Analyze("  !_control+k+control_+_control+_b  ");

        Assert.Equal(CustomCommandKind.HotkeySequence, analysis.Kind);
        Assert.True(analysis.IsValid);
        Assert.Null(analysis.Error);
        Assert.Equal(5, analysis.Sequence!.Steps.Count);
        Assert.Null(analysis.Conditional);
        Assert.Null(analysis.LaunchTarget);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("!  ")]
    [InlineData("!_control+k+contrl")]
    [InlineData("!!a")]
    public void Analyze_InvalidHotkeySequence_IsInvalidWithErrorAndNoPayload(string command)
    {
        var analysis = CustomCommandAnalyzer.Analyze(command);

        Assert.Equal(CustomCommandKind.HotkeySequence, analysis.Kind);
        Assert.False(analysis.IsValid);
        Assert.False(string.IsNullOrEmpty(analysis.Error));
        Assert.Null(analysis.Sequence);
        Assert.Null(analysis.Conditional);
        Assert.Null(analysis.LaunchTarget);
    }

    [Theory]
    [InlineData("#[Chrome_WidgetWin_1]:(_control+k+control_+_control+_b)?(win)")]
    [InlineData("@[,Calculator]:(escape)")]
    [InlineData("  #[MultitaskingViewFrame]:(escape)?(_control+_alt+tab)\n")]
    public void Analyze_HashOrAtPrefix_IsConditional(string command)
    {
        var analysis = CustomCommandAnalyzer.Analyze(command);

        Assert.Equal(CustomCommandKind.ConditionalHotkey, analysis.Kind);
        Assert.True(analysis.IsValid);
        Assert.NotNull(analysis.Conditional);
        Assert.Null(analysis.Sequence);
        Assert.Null(analysis.LaunchTarget);
    }

    [Theory]
    [InlineData("#[]:(win)")]
    [InlineData("#[Notepad]:(win)?(nope)")]
    [InlineData("@notepad")]
    public void Analyze_InvalidConditional_IsInvalid(string command)
    {
        var analysis = CustomCommandAnalyzer.Analyze(command);

        Assert.Equal(CustomCommandKind.ConditionalHotkey, analysis.Kind);
        Assert.False(analysis.IsValid);
        Assert.NotNull(analysis.Error);
        Assert.Null(analysis.Conditional);
    }

    [Theory]
    [InlineData("notepad.exe", "notepad.exe")]
    [InlineData("  C:\\Program Files\\App\\app.exe  ", "C:\\Program Files\\App\\app.exe")]
    [InlineData("https://example.com/?q=a+b", "https://example.com/?q=a+b")]
    [InlineData("ms-settings:display", "ms-settings:display")]
    [InlineData("_control+k", "_control+k")]
    public void Analyze_OtherText_IsLaunchWithTrimmedTarget(string command, string expected)
    {
        var analysis = CustomCommandAnalyzer.Analyze(command);

        Assert.Equal(new CustomCommandAnalysis(CustomCommandKind.Launch, true, null, null, null, expected), analysis);
    }

    [Theory]
    [InlineData("calc.exe\nnotepad.exe", 9)]
    [InlineData("calc\0.exe", 5)]
    [InlineData("a\tb", 2)]
    [InlineData("a\u007Fb", 2)]
    [InlineData("a\u0085b", 2)]
    [InlineData("ab\u009F", 3)]
    public void Analyze_LaunchWithControlCharacter_IsInvalid(string command, int column)
    {
        var analysis = CustomCommandAnalyzer.Analyze(command);

        Assert.Equal(CustomCommandKind.Launch, analysis.Kind);
        Assert.False(analysis.IsValid);
        Assert.Contains($"column {column}", analysis.Error);
        Assert.Null(analysis.LaunchTarget);
    }

    [Fact]
    public void Analyze_RandomInput_NeverThrowsAndIsConsistent()
    {
        var random = new Random(77);
        for (var i = 0; i < 10000; i++)
        {
            var chars = new char[random.Next(0, 30)];
            for (var j = 0; j < chars.Length; j++)
            {
                // Mostly the syntax characters, sometimes anything in the BMP (control characters included).
                chars[j] = random.Next(4) == 0 ? (char)random.Next(0, 0xFFFF) : "!#@[](),:?_+ ab\t"[random.Next(16)];
            }

            var analysis = CustomCommandAnalyzer.Analyze(new string(chars));

            Assert.Equal(analysis.IsValid, analysis.Error is null);
            var payloads = (analysis.Sequence is null ? 0 : 1) + (analysis.Conditional is null ? 0 : 1) + (analysis.LaunchTarget is null ? 0 : 1);
            Assert.Equal(analysis.IsValid && analysis.Kind != CustomCommandKind.Empty ? 1 : 0, payloads);
        }
    }
}
