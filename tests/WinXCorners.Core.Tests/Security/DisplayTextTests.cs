using System.Globalization;
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
    [InlineData("a\u2028b", @"a\u2028b")]
    [InlineData("a\u2029b", @"a\u2029b")]
    [InlineData("a\u0085b", @"a\u0085b")]
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

    [Theory]
    [InlineData(@"C:\Program Files\Tool\tool.exe", "\u201CC:\\Program Files\\Tool\\tool.exe\u201D")]
    [InlineData("say \"hi\" 'x'", "\u201Csay \"hi\" 'x'\u201D")]
    [InlineData("", "\u201C\u201D")]
    [InlineData("a\u201Cb\u201Dc", "\u201Ca\\u{201C}b\\u{201D}c\u201D")]
    [InlineData("a\nb\u2028c\u202Ed", "\u201Ca\\u{000A}b\\u{2028}c\\u{202E}d\u201D")]
    [InlineData("\U0001F600", "\u201C\U0001F600\u201D")]
    [InlineData(@"\u{0041}", "\u201C\\u{005C}u{0041}\u201D")]
    [InlineData(@"\u0041 \u{ \u \", "\u201C\\u0041 \\u{005C}u{ \\u \\\u201D")]
    public void Frame_EscapesOnlyWhatCouldCloseTheFrameBreakALineOrHide(string value, string expected)
    {
        Assert.Equal(expected, DisplayText.Frame(value));
    }

    // Unpaired surrogates cannot be stored in attribute data (UTF-8), hence a Fact rather than InlineData rows.
    [Fact]
    public void Frame_EscapesUnpairedSurrogatesButKeepsPairs()
    {
        Assert.Equal("\u201Cx\\u{D800}y\\u{DC00}\u201D", DisplayText.Frame("x\uD800y\uDC00"));
        Assert.Equal("\u201C\\u{DC00}\\u{D800}\u201D", DisplayText.Frame("\uDC00\uD800"));
        Assert.Equal("\u201C\uD83D\uDE00\\u{D83D}\u201D", DisplayText.Frame("\uD83D\uDE00\uD83D"));
    }

    [Fact]
    public void Frame_NullIsAnEmptyFrameAndLongValuesAreNeverTruncated()
    {
        Assert.Equal("\u201C\u201D", DisplayText.Frame(null));
        var text = new string('x', 10_000);
        Assert.Equal("\u201C" + text + "\u201D", DisplayText.Frame(text));
    }

    /// <summary>
    /// Property: for random and hostile strings the framed text has exactly one opening and one closing mark (at the
    /// ends), contains nothing that can break a line or reorder text, and decodes back to exactly the original.
    /// </summary>
    [Fact]
    public void Frame_RandomHostileStrings_DecodeBackExactly()
    {
        var random = new Random(20261117);
        const string alphabet = "a Z:\\/\"'(){}u-><\u201C\u201D\u00AB\u00BB\n\r\t\0\u0085\u2028\u2029\u202E\u2066\u200B\u061C\uFEFF\uD800\uDBFF\uDC00\uDFFF\u00E9\u0301";
        for (var round = 0; round < 20_000; round++)
        {
            var value = new string(Enumerable.Range(0, random.Next(0, 24)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var framed = DisplayText.Frame(value);
            Assert.Equal(DisplayText.FrameOpen, framed[0]);
            Assert.Equal(DisplayText.FrameClose, framed[^1]);
            var inner = framed[1..^1];
            Assert.DoesNotContain(DisplayText.FrameOpen, inner);
            Assert.DoesNotContain(DisplayText.FrameClose, inner);
            for (var i = 0; i < inner.Length; i++)
            {
                var category = char.GetUnicodeCategory(inner[i]);
                Assert.False(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator, $"U+{(int)inner[i]:X4} left unescaped");
                if (char.IsHighSurrogate(inner[i]))
                {
                    Assert.True(char.IsLowSurrogate(inner[++i]));
                }
                else
                {
                    Assert.False(char.IsLowSurrogate(inner[i]));
                }
            }

            Assert.Equal(value, FramedValues.Decode(inner));
            Assert.Equal([value], FramedValues.Parse("x " + framed + " y"));
        }
    }
}
