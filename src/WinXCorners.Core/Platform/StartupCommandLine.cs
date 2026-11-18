namespace WinXCorners.Core.Platform;

/// <summary>What the Run value says about this executable.</summary>
public enum RunEntryState
{
    /// <summary>There is no value (or it is blank).</summary>
    None,

    /// <summary>The value starts exactly this executable.</summary>
    ThisExecutable,

    /// <summary>The value starts some other program (often an older copy that was moved).</summary>
    OtherProgram,

    /// <summary>
    /// The value is not quoted and its program path contains spaces, so Windows may start a different, shorter path
    /// first (<c>C:\Program.exe</c> for <c>C:\Program Files\...</c>). It is never counted as this executable.
    /// </summary>
    UnquotedWithSpaces,

    /// <summary>The value cannot be read as a command line (an unterminated quote, an empty program).</summary>
    Malformed
}

/// <summary>
/// Builds and reads the HKCU <c>Run</c> value. The app compares the stored value with its own path to notice a
/// stale entry (for example after the app was moved) and asks, rather than silently rewriting someone else's entry.
/// </summary>
public static class StartupCommandLine
{
    /// <summary>
    /// The value to store: the normalized path, always quoted so a path with spaces can never be split into a
    /// shorter, attacker-placed program name (the classic unquoted-path problem).
    /// </summary>
    /// <exception cref="ArgumentException">The path is blank, not fully qualified, or contains a quote or control character.</exception>
    public static string Build(string executablePath)
    {
        var normalized = WindowsPath.Normalize(executablePath);
        if (!WindowsPath.IsFullyQualified(normalized))
        {
            throw new ArgumentException("The startup entry needs a fully qualified executable path.", nameof(executablePath));
        }

        if (normalized.Contains('"') || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("The executable path contains a character that cannot appear in a file name.", nameof(executablePath));
        }

        return "\"" + normalized + "\"";
    }

    /// <summary>
    /// Extracts the program path from a Run value, ignoring trailing arguments. A quoted value yields the text inside
    /// the quotes. An unquoted value yields everything up to the first <c>.exe</c> that ends a token, or, without
    /// one, up to the first whitespace. Returns false for blank values, an unterminated quote, or an empty path.
    /// </summary>
    public static bool TryGetExecutablePath(string? runValue, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(runValue))
        {
            return false;
        }

        var text = runValue.Trim();
        string candidate;
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close < 0)
            {
                return false;
            }

            // Text glued to the closing quote ("C:\a.exe"x) is not a separate argument; reject rather than guess.
            if (close + 1 < text.Length && !char.IsWhiteSpace(text[close + 1]))
            {
                return false;
            }

            candidate = text[1..close];
        }
        else
        {
            candidate = UnquotedProgram(text);
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        path = WindowsPath.Normalize(candidate);
        return true;
    }

    /// <summary>
    /// True when the Run value launches exactly <paramref name="executablePath"/> (normalized, case-insensitive) and
    /// nothing else could be started in its place: an unquoted value whose program path contains spaces never counts.
    /// </summary>
    public static bool PointsTo(string? runValue, string? executablePath) =>
        Classify(runValue, executablePath) == RunEntryState.ThisExecutable;

    /// <summary>Classifies a Run value against this executable (see <see cref="RunEntryState"/>).</summary>
    public static RunEntryState Classify(string? runValue, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(runValue))
        {
            return RunEntryState.None;
        }

        if (!TryGetExecutablePath(runValue, out var path))
        {
            return RunEntryState.Malformed;
        }

        // CreateProcess splits an unquoted command line at each space and tries the pieces from the shortest, so
        // "C:\Program Files\App\app.exe" can start C:\Program.exe. Such a value is not "this executable" however
        // well its full text matches, and is reported as stale so the user can save it again (quoted).
        if (runValue.TrimStart()[0] != '"' && path.Any(char.IsWhiteSpace))
        {
            return RunEntryState.UnquotedWithSpaces;
        }

        return !string.IsNullOrWhiteSpace(executablePath) &&
               string.Equals(path, WindowsPath.Normalize(executablePath), StringComparison.OrdinalIgnoreCase)
            ? RunEntryState.ThisExecutable
            : RunEntryState.OtherProgram;
    }

    private static string UnquotedProgram(string text)
    {
        const string Extension = ".exe";
        var search = 0;
        while (true)
        {
            var index = text.IndexOf(Extension, search, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            var end = index + Extension.Length;
            if (end == text.Length || char.IsWhiteSpace(text[end]))
            {
                return text[..end];
            }

            search = index + 1;
        }

        var space = text.IndexOfAny([' ', '\t']);
        return space < 0 ? text : text[..space];
    }
}
