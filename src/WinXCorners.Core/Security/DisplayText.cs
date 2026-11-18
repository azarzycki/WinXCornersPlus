using System.Globalization;
using System.Text;

namespace WinXCorners.Core.Security;

/// <summary>
/// Makes untrusted text (command lines and names read from settings.json) safe to show in a one-line prompt.
/// Control characters and line/paragraph separators would let a crafted value start a fake new line in an
/// approval list, and bidirectional or other invisible format characters (such as U+202E) could make a command
/// read differently from what runs; all of them are shown as visible escapes instead.
/// </summary>
public static class DisplayText
{
    /// <summary>Characters kept from the escaped text before <see cref="Ellipsis"/> is appended.</summary>
    public const int MaxLength = 120;

    public const string Ellipsis = "…";

    /// <summary>Opens a value framed by <see cref="Frame"/> (U+201C LEFT DOUBLE QUOTATION MARK).</summary>
    public const char FrameOpen = '“';

    /// <summary>Closes a value framed by <see cref="Frame"/> (U+201D RIGHT DOUBLE QUOTATION MARK).</summary>
    public const char FrameClose = '”';

    /// <summary>
    /// Escapes invisible characters, then truncates to <paramref name="maxLength"/> characters plus an ellipsis.
    /// For log lines only: an approval prompt must show the whole value (use <see cref="Frame"/>).
    /// </summary>
    public static string ForDisplay(string? value, int maxLength = MaxLength)
    {
        var escaped = Escape(value ?? string.Empty);
        if (escaped.Length <= maxLength)
        {
            return escaped;
        }

        // Do not split a surrogate pair at the cut.
        var cut = maxLength;
        if (cut > 0 && char.IsHighSurrogate(escaped[cut - 1]))
        {
            cut--;
        }

        return escaped[..cut] + Ellipsis;
    }

    /// <summary>
    /// Replaces control, format, line-separator and paragraph-separator characters with <c>\n</c>, <c>\r</c>,
    /// <c>\t</c> or <c>\uXXXX</c>. Readable, but not reversible (a literal <c>\n</c> looks the same as an escaped
    /// line feed), so use it for logs and <see cref="Frame"/> wherever the exact value matters.
    /// </summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var replacement = c switch
            {
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                _ when IsUnsafe(c) => @"\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
                _ => null
            };

            if (replacement is null)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(value, 0, i, value.Length + 16);
            builder.Append(replacement);
        }

        return builder?.ToString() ?? value;
    }

    /// <summary>
    /// Shows a settings-derived value inside <see cref="FrameOpen"/> … <see cref="FrameClose"/>, complete (never
    /// truncated) and in a form that cannot close its own frame. Ordinary text, Windows paths and ASCII quotes
    /// appear unchanged. These are written as <c>\u{XXXX}</c> (one UTF-16 code unit, upper-case hex): the two
    /// frame characters; control, format, line- and paragraph-separator characters; unpaired surrogates; and a
    /// backslash that is directly followed by <c>u{</c>, so a value can never spell out a fake escape.
    /// Decoding every <c>\u{XXXX}</c> between the frame characters gives back exactly the original value.
    /// </summary>
    public static string Frame(string? value)
    {
        var text = value ?? string.Empty;
        var builder = new StringBuilder(text.Length + 2);
        builder.Append(FrameOpen);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var escape = c switch
            {
                FrameOpen or FrameClose => true,
                '\\' => i + 2 < text.Length && text[i + 1] == 'u' && text[i + 2] == '{',
                _ when char.IsHighSurrogate(c) => !(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])),
                _ when char.IsLowSurrogate(c) => !(i > 0 && char.IsHighSurrogate(text[i - 1])),
                _ => IsUnsafe(c)
            };

            if (escape)
            {
                builder.Append(@"\u{").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('}');
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append(FrameClose).ToString();
    }

    /// <summary>
    /// Characters that can break a line (Cc, including CR, LF and NEL; Zl; Zp) or are invisible and can reorder
    /// or disguise text (Cf, which includes every bidirectional control).
    /// </summary>
    private static bool IsUnsafe(char c) =>
        char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) is
            UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
