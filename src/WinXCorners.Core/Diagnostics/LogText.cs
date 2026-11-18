namespace WinXCorners.Core.Diagnostics;

/// <summary>Helpers for the Log window's text box.</summary>
public static class LogText
{
    /// <summary>
    /// The last <paramref name="maxLines"/> lines of <paramref name="text"/>, with the original line
    /// separators preserved, so the text box cannot grow without bound. Lines end at '\n' (so "\r\n"
    /// works too); a single trailing line break terminates the last line rather than starting an empty
    /// one. Null or a non-positive <paramref name="maxLines"/> gives "".
    /// </summary>
    public static string TrimToLastLines(string? text, int maxLines)
    {
        if (string.IsNullOrEmpty(text) || maxLines <= 0)
        {
            return string.Empty;
        }

        var searchEnd = text.Length - 1;
        if (text[searchEnd] == '\n')
        {
            searchEnd--;
        }

        var found = 0;
        for (var i = searchEnd; i >= 0; i--)
        {
            if (text[i] == '\n' && ++found == maxLines)
            {
                return text[(i + 1)..];
            }
        }

        return text;
    }
}
