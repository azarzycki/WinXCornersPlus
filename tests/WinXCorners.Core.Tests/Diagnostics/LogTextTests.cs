using WinXCorners.Core.Diagnostics;

namespace WinXCorners.Core.Tests.Diagnostics;

public class LogTextTests
{
    [Theory]
    [InlineData("a\r\nb\r\nc", 2, "b\r\nc")]
    [InlineData("a\nb\nc", 1, "c")]
    [InlineData("a\nb\nc", 3, "a\nb\nc")]
    [InlineData("a\nb\nc", 10, "a\nb\nc")]
    [InlineData("a\r\nb\r\nc\r\n", 2, "b\r\nc\r\n")]   // trailing break ends the last line
    [InlineData("a\nb\nc\n", 1, "c\n")]
    [InlineData("single", 1, "single")]
    [InlineData("a\n\n\nb", 2, "\nb")]                  // empty lines count
    [InlineData("\n", 1, "\n")]
    [InlineData("\n\n", 1, "\n")]
    [InlineData("a\nb", 0, "")]
    [InlineData("a\nb", -5, "")]
    [InlineData("", 3, "")]
    public void TrimToLastLines(string text, int maxLines, string expected)
    {
        Assert.Equal(expected, LogText.TrimToLastLines(text, maxLines));
    }

    [Fact]
    public void NullTextIsEmpty()
    {
        Assert.Equal(string.Empty, LogText.TrimToLastLines(null, 5));
    }

    [Fact]
    public void LongLogIsBoundedAndKeepsTheNewestLines()
    {
        var text = string.Join("\r\n", Enumerable.Range(0, 10_000).Select(static i => $"line {i}"));
        var trimmed = LogText.TrimToLastLines(text, 500);
        var lines = trimmed.Split("\r\n");
        Assert.Equal(500, lines.Length);
        Assert.Equal("line 9500", lines[0]);
        Assert.Equal("line 9999", lines[^1]);
        Assert.Equal(trimmed, LogText.TrimToLastLines(trimmed, 500));
    }
}
