using System.Text;

namespace WinXCorners.App;

/// <summary>
/// Joins arguments into one Windows command line so that <c>CommandLineToArgvW</c> (and the .NET runtime) splits it
/// back into exactly the same list. Used for the restart command lines this app builds itself.
/// </summary>
internal static class CommandLineQuoting
{
    internal static string Join(IEnumerable<string> arguments)
    {
        return string.Join(" ", arguments.Select(Quote));
    }

    /// <summary>
    /// Quotes one argument when needed. Backslashes are literal except directly before a quote, where each must be
    /// doubled, and an embedded quote is written as <c>\"</c>.
    /// </summary>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
        {
            return argument;
        }

        var builder = new StringBuilder(argument.Length + 2);
        builder.Append('"');
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            builder.Append(c);
            backslashes = 0;
        }

        // Backslashes before the closing quote must be doubled so they do not escape it.
        builder.Append('\\', backslashes * 2);
        builder.Append('"');
        return builder.ToString();
    }
}
