using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class DisplayTextTests
{
    [Theory]
    [InlineData("notepad.exe", "notepad.exe")]
    [InlineData("a\nb", @"a\nb")]
    [InlineData("a\r\tb", @"a\r\tb")]
    [InlineData("a\0b", @"a\u0000b")]
    [InlineData("evil\u202Etxt.exe", @"evil\u202Etxt.exe")]
    [InlineData("zero\u200Bwidth", @"zero\u200Bwidth")]
    [InlineData("caf\u00e9", "caf\u00e9")]
    public void Escape_MakesInvisibleCharactersVisible(string input, string expected)
    {
        Assert.Equal(expected, DisplayText.Escape(input));
    }

    [Fact]
    public void ForDisplay_TruncatesLongTextTo120CharactersPlusEllipsis()
    {
        var text = new string('x', 500);
        var shown = DisplayText.ForDisplay(text);
        Assert.Equal(new string('x', 120) + DisplayText.Ellipsis, shown);
        Assert.Equal(new string('x', 120), DisplayText.ForDisplay(new string('x', 120)));
    }

    [Fact]
    public void ForDisplay_DoesNotSplitSurrogatePairs()
    {
        var text = new string('x', 119) + "\U0001F600" + "tail";
        var shown = DisplayText.ForDisplay(text);
        Assert.Equal(new string('x', 119) + DisplayText.Ellipsis, shown);
    }

    [Fact]
    public void ForDisplay_HandlesNull()
    {
        Assert.Equal(string.Empty, DisplayText.ForDisplay(null));
    }
}
