using System.Globalization;
using System.Text;

namespace WinXCorners.Core.Security;

/// <summary>
/// Makes untrusted text (command lines and names read from settings.json) safe to show in a one-line prompt.
/// Control characters would let a crafted value start a fake new line in an approval list, and bidirectional
/// or other invisible format characters (such as U+202E) could make a command read differently from what runs;
/// both are shown as visible escapes instead.
/// </summary>
public static class DisplayText
{
    /// <summary>Characters kept from the escaped text before <see cref="Ellipsis"/> is appended.</summary>
    public const int MaxLength = 120;

    public const string Ellipsis = "…";

    /// <summary>Escapes invisible characters, then truncates to <see cref="MaxLength"/> characters plus an ellipsis.</summary>
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

    /// <summary>Replaces control and format characters with <c>\n</c>, <c>\r</c>, <c>\t</c> or <c>\uXXXX</c>.</summary>
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
                _ when char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format =>
                    @"\u" + ((int)c).ToString("X4", CultureInfo.InvariantCulture),
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
}
