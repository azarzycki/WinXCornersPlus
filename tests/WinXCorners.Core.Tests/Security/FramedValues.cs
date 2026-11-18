using System.Globalization;
using System.Text;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

/// <summary>
/// An independent reader for lines that contain <see cref="DisplayText.Frame"/>d values, written from the documented
/// format only: a value runs from U+201C to the next U+201D, and every <c>\u{XXXX}</c> inside it is one code unit.
/// </summary>
internal static class FramedValues
{
    /// <summary>The decoded values of every frame in <paramref name="line"/>, in order.</summary>
    public static List<string> Parse(string line)
    {
        var values = new List<string>();
        var i = 0;
        while ((i = line.IndexOf(DisplayText.FrameOpen, i)) >= 0)
        {
            var end = line.IndexOf(DisplayText.FrameClose, i + 1);
            Assert.True(end > i, "unterminated frame in: " + line);
            values.Add(Decode(line[(i + 1)..end]));
            i = end + 1;
        }

        return values;
    }

    /// <summary>Text outside the frames, with each frame replaced by <c>{}</c>.</summary>
    public static string Skeleton(string line)
    {
        var builder = new StringBuilder();
        var inside = false;
        foreach (var c in line)
        {
            if (!inside && c == DisplayText.FrameOpen)
            {
                inside = true;
                builder.Append("{}");
            }
            else if (inside && c == DisplayText.FrameClose)
            {
                inside = false;
            }
            else if (!inside)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static string Decode(string framed)
    {
        Assert.DoesNotContain(DisplayText.FrameOpen, framed);
        var builder = new StringBuilder();
        for (var i = 0; i < framed.Length; i++)
        {
            if (framed[i] == '\\' && i + 7 < framed.Length && framed.AsSpan(i + 1, 2).SequenceEqual("u{") &&
                framed[i + 7] == '}' &&
                ushort.TryParse(framed.AsSpan(i + 3, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit))
            {
                builder.Append((char)unit);
                i += 7;
            }
            else
            {
                builder.Append(framed[i]);
            }
        }

        return builder.ToString();
    }
}
