namespace WinXCorners.Core.Platform;

/// <summary>
/// Lexical Windows path handling implemented with plain string logic. <see cref="System.IO.Path"/> follows the
/// host OS, so on Linux it would treat <c>C:\Program Files</c> as a relative file name; security decisions about
/// Windows paths must give the same answer on every OS, hence this class.
/// </summary>
/// <remarks>
/// Nothing here touches the file system: symbolic links and junctions are not resolved. Callers that need the
/// physical location must resolve it first (the app passes <c>AppContext.BaseDirectory</c>, which is already final).
/// </remarks>
public static class WindowsPath
{
    private const char Separator = '\\';

    /// <summary>
    /// Canonical lexical form: surrounding whitespace and quotes removed, <c>/</c> turned into <c>\</c>, repeated
    /// separators collapsed (except the leading <c>\\</c> of a UNC path), <c>.</c> and <c>..</c> resolved, the
    /// trailing separator removed except for a drive root, and the drive letter upper-cased.
    /// <c>..</c> never climbs above a drive root or a UNC share. The <c>\\?\</c> and <c>\\.\</c> device prefixes
    /// are unwrapped so that <c>\\?\C:\x</c> and <c>C:\x</c> compare equal.
    /// Returns an empty string for null or blank input, and <c>.</c> for a relative path that resolves to nothing.
    /// </summary>
    public static string Normalize(string? path)
    {
        var parsed = Parse(path);
        return parsed.ToString();
    }

    /// <summary>True for a drive-rooted path (<c>X:\…</c>) or a UNC path (<c>\\server\share…</c>).</summary>
    public static bool IsFullyQualified(string? path)
    {
        var parsed = Parse(path);
        return parsed.Kind is RootKind.Drive or RootKind.Unc;
    }

    /// <summary>True for <c>\\server\share…</c> (including the <c>\\?\UNC\</c> form).</summary>
    public static bool IsUnc(string? path) => Parse(path).Kind == RootKind.Unc;

    /// <summary>
    /// True when <paramref name="path"/> is <paramref name="root"/> or lies inside it. Comparison is
    /// case-insensitive and by whole segments, so <c>C:\Program Files (x86)</c> is not under <c>C:\Program Files</c>.
    /// Both paths must be fully qualified; relative input is never "under" anything.
    /// </summary>
    public static bool IsUnder(string? path, string? root)
    {
        var child = Parse(path);
        var parent = Parse(root);
        if (child.Kind is not (RootKind.Drive or RootKind.Unc) || child.Kind != parent.Kind ||
            !string.Equals(child.Root, parent.Root, StringComparison.OrdinalIgnoreCase) ||
            child.Segments.Count < parent.Segments.Count)
        {
            return false;
        }

        for (var i = 0; i < parent.Segments.Count; i++)
        {
            if (!string.Equals(child.Segments[i], parent.Segments[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Joins a directory and a relative file name and normalizes the result. As with <c>Path.Combine</c>, a fully
    /// qualified <paramref name="file"/> replaces the directory. <c>..</c> in <paramref name="file"/> is resolved,
    /// so callers that must stay inside <paramref name="directory"/> should check the result with <see cref="IsUnder"/>.
    /// </summary>
    public static string Combine(string? directory, string? file)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return Normalize(directory);
        }

        if (IsFullyQualified(file) || string.IsNullOrWhiteSpace(directory))
        {
            return Normalize(file);
        }

        return Normalize(StripQuotes(directory) + Separator + StripQuotes(file));
    }

    private static string StripQuotes(string value) => value.Trim().Trim('"').Trim();

    private enum RootKind
    {
        Empty,
        Relative,
        /// <summary><c>X:</c> with no separator: relative to that drive's current directory.</summary>
        DriveRelative,
        /// <summary><c>\foo</c>: relative to the current drive's root.</summary>
        RootRelative,
        Drive,
        Unc
    }

    private sealed record ParsedPath(RootKind Kind, string Root, List<string> Segments)
    {
        public override string ToString()
        {
            var tail = string.Join(Separator, Segments);
            return Kind switch
            {
                RootKind.Empty => string.Empty,
                RootKind.Relative => tail.Length == 0 ? "." : tail,
                RootKind.DriveRelative => Root + tail,
                RootKind.RootRelative => Separator + tail,
                RootKind.Drive => Root + Separator + tail,
                _ => tail.Length == 0 ? Root : Root + Separator + tail
            };
        }
    }

    private static ParsedPath Parse(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new ParsedPath(RootKind.Empty, string.Empty, []);
        }

        var text = StripQuotes(path).Replace('/', Separator);
        if (text.Length == 0)
        {
            return new ParsedPath(RootKind.Empty, string.Empty, []);
        }

        // Device-path prefixes: \\?\C:\x and \\.\C:\x mean C:\x; \\?\UNC\server\share means \\server\share.
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal) || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            var rest = text[4..];
            text = rest.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase) ? @"\\" + rest[4..] : rest;
        }

        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var parts = Split(text);
            if (parts.Count < 2)
            {
                // "\\server" alone has no share: it cannot name a file, so treat it as a root-relative remnant.
                return new ParsedPath(RootKind.RootRelative, string.Empty, Resolve(parts, rooted: true));
            }

            var root = @"\\" + parts[0] + Separator + parts[1];
            return new ParsedPath(RootKind.Unc, root, Resolve(parts.Skip(2), rooted: true));
        }

        if (text.Length >= 2 && text[1] == ':' && char.IsAsciiLetter(text[0]))
        {
            var root = char.ToUpperInvariant(text[0]) + ":";
            var rest = text[2..];
            return rest.StartsWith(Separator)
                ? new ParsedPath(RootKind.Drive, root, Resolve(Split(rest), rooted: true))
                : new ParsedPath(RootKind.DriveRelative, root, Resolve(Split(rest), rooted: false));
        }

        if (text[0] == Separator)
        {
            return new ParsedPath(RootKind.RootRelative, string.Empty, Resolve(Split(text), rooted: true));
        }

        return new ParsedPath(RootKind.Relative, string.Empty, Resolve(Split(text), rooted: false));
    }

    private static List<string> Split(string text) =>
        text.Split(Separator, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> Resolve(IEnumerable<string> segments, bool rooted)
    {
        var stack = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else if (!rooted)
                {
                    // A relative path may legitimately start above its base; keep the "..".
                    stack.Add(segment);
                }

                // Rooted: ".." at the root stays at the root, exactly as Windows resolves it.
                continue;
            }

            stack.Add(segment);
        }

        return stack;
    }
}
