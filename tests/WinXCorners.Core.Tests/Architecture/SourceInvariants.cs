using System.Text.RegularExpressions;

namespace WinXCorners.Core.Tests.Architecture;

/// <summary>A source file as the guard rules see it.</summary>
internal sealed record SourceFile(string RelativePath, string Raw)
{
    private (string Code, string NoComments)? _views;

    internal string FileName => Path.GetFileName(RelativePath);

    /// <summary>Comments and literal contents blanked.</summary>
    internal string Code => (_views ??= CSharpSourceText.Strip(Raw)).Code;

    /// <summary>Comments blanked, literals kept.</summary>
    internal string NoComments => (_views ??= CSharpSourceText.Strip(Raw)).NoComments;

    internal string Where(int index) => $"{RelativePath}:{CSharpSourceText.LineOf(Raw, index)}";
}

/// <summary>The security invariants of the source, as functions that return violations ("file:line: message").</summary>
internal static partial class SourceInvariants
{
    internal static IEnumerable<string> Matches(SourceFile file, string view, Regex pattern, string message) =>
        pattern.Matches(view).Select(m => $"{file.Where(m.Index)}: {message}");

    /// <summary>A pattern may appear only in the listed files.</summary>
    internal static IEnumerable<string> OnlyIn(IEnumerable<SourceFile> files, Func<SourceFile, string> view, Regex pattern,
        IReadOnlyCollection<string> allowedFileNames, string what) =>
        files.Where(f => !allowedFileNames.Contains(f.FileName, StringComparer.OrdinalIgnoreCase))
            .SelectMany(f => Matches(f, view(f), pattern, $"{what} is only allowed in {string.Join(", ", allowedFileNames)}"));

    internal static IEnumerable<string> Nowhere(IEnumerable<SourceFile> files, Func<SourceFile, string> view, Regex pattern, string what) =>
        files.SelectMany(f => Matches(f, view(f), pattern, $"{what} is not allowed"));

    /// <summary>Bare catch blocks, empty catch blocks, and catch-all (Exception) blocks with no statements.</summary>
    internal static IEnumerable<string> BadCatches(SourceFile file)
    {
        var code = file.Code;
        foreach (Match match in CatchKeyword().Matches(code))
        {
            var i = match.Index + match.Length;
            i = SkipWhitespace(code, i);
            string? type = null;
            if (i < code.Length && code[i] == '(')
            {
                var close = MatchingClose(code, i, '(', ')');
                if (close < 0)
                {
                    continue;
                }

                type = code[(i + 1)..close].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                i = SkipWhitespace(code, close + 1);
                if (code.AsSpan(i).StartsWith("when", StringComparison.Ordinal))
                {
                    i = SkipWhitespace(code, i + 4);
                    var whenClose = i < code.Length && code[i] == '(' ? MatchingClose(code, i, '(', ')') : -1;
                    if (whenClose < 0)
                    {
                        continue;
                    }

                    i = SkipWhitespace(code, whenClose + 1);
                }
            }

            if (i >= code.Length || code[i] != '{')
            {
                continue;
            }

            var end = MatchingClose(code, i, '{', '}');
            if (end < 0)
            {
                continue;
            }

            var bodyCode = code[(i + 1)..end];
            var bodyRaw = file.Raw[(i + 1)..end];
            if (type is null)
            {
                yield return $"{file.Where(match.Index)}: bare 'catch' (catch a specific exception type)";
            }
            else if (string.IsNullOrWhiteSpace(bodyRaw))
            {
                yield return $"{file.Where(match.Index)}: empty catch block";
            }
            else if (type is "Exception" or "System.Exception" && string.IsNullOrWhiteSpace(bodyCode))
            {
                yield return $"{file.Where(match.Index)}: catch (Exception) that only has a comment swallows every error";
            }
        }
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int MatchingClose(string text, int open, char openChar, char closeChar)
    {
        var depth = 0;
        for (var k = open; k < text.Length; k++)
        {
            if (text[k] == openChar)
            {
                depth++;
            }
            else if (text[k] == closeChar && --depth == 0)
            {
                return k;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"\bcatch\b")]
    private static partial Regex CatchKeyword();
}
