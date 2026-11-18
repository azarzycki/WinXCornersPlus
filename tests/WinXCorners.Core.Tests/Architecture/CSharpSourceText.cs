namespace WinXCorners.Core.Tests.Architecture;

/// <summary>
/// A small C# lexer for the source guard tests: blanks comments, and optionally string/char literal contents, while
/// keeping every line break, so a match's index still gives the right line number.
/// </summary>
/// <remarks>
/// Handles // and /* */ comments, regular, verbatim (@"..."), raw ("""...""") and interpolated strings, including
/// code inside interpolation holes (which stays code), and char literals. It does not need to be a full parser:
/// it only has to keep text inside literals and comments from being mistaken for code, and vice versa.
/// </remarks>
internal static class CSharpSourceText
{
    private enum Kind
    {
        Code,
        Regular,
        Verbatim,
        Raw
    }

    private sealed class Frame
    {
        internal Kind Kind;
        internal bool Interpolated;
        internal int Dollars;
        internal int Quotes;
        internal int BraceDepth;
    }

    /// <summary>
    /// <c>Code</c>: comments and literal contents blanked. <c>NoComments</c>: only comments blanked (literals kept).
    /// </summary>
    internal static (string Code, string NoComments) Strip(string source)
    {
        var code = source.ToCharArray();
        var noComments = source.ToCharArray();
        var length = source.Length;
        var stack = new Stack<Frame>();
        stack.Push(new Frame { Kind = Kind.Code });

        void BlankBoth(int index)
        {
            if (index < length && source[index] is not ('\n' or '\r'))
            {
                code[index] = ' ';
                noComments[index] = ' ';
            }
        }

        void BlankCode(int index)
        {
            if (index < length && source[index] is not ('\n' or '\r'))
            {
                code[index] = ' ';
            }
        }

        char At(int index) => index < length ? source[index] : '\0';

        var i = 0;
        while (i < length)
        {
            var frame = stack.Peek();
            var c = source[i];
            if (frame.Kind == Kind.Code)
            {
                if (c == '/' && At(i + 1) == '/')
                {
                    while (i < length && source[i] != '\n')
                    {
                        BlankBoth(i++);
                    }

                    continue;
                }

                if (c == '/' && At(i + 1) == '*')
                {
                    BlankBoth(i++);
                    BlankBoth(i++);
                    while (i < length && !(source[i] == '*' && At(i + 1) == '/'))
                    {
                        BlankBoth(i++);
                    }

                    BlankBoth(i++);
                    BlankBoth(i++);
                    continue;
                }

                if (c == '\'')
                {
                    i++;
                    while (i < length && source[i] != '\'' && source[i] != '\n')
                    {
                        if (source[i] == '\\')
                        {
                            BlankCode(i++);
                        }

                        BlankCode(i++);
                    }

                    i++;
                    continue;
                }

                // String start: any mix of $ and @ prefixes, then quotes. Not part of an identifier (e.g. @class).
                var j = i;
                var dollars = 0;
                var verbatim = false;
                while (j < length && source[j] is '$' or '@')
                {
                    if (source[j] == '$')
                    {
                        dollars++;
                    }
                    else
                    {
                        verbatim = true;
                    }

                    j++;
                }

                if (At(j) == '"' && (i == 0 || !IsIdentifierChar(source[i - 1])))
                {
                    var quotes = 0;
                    while (At(j + quotes) == '"')
                    {
                        quotes++;
                    }

                    if (quotes >= 3)
                    {
                        stack.Push(new Frame { Kind = Kind.Raw, Interpolated = dollars > 0, Dollars = dollars, Quotes = quotes });
                        i = j + quotes;
                    }
                    else
                    {
                        stack.Push(new Frame { Kind = verbatim ? Kind.Verbatim : Kind.Regular, Interpolated = dollars > 0, Dollars = 1 });
                        i = j + 1;
                    }

                    continue;
                }

                if (c == '{')
                {
                    frame.BraceDepth++;
                }
                else if (c == '}')
                {
                    if (frame.BraceDepth == 0 && stack.Count > 1)
                    {
                        stack.Pop(); // End of an interpolation hole.
                        i++;
                        continue;
                    }

                    frame.BraceDepth--;
                }

                i++;
                continue;
            }

            // Inside a string literal.
            switch (frame.Kind)
            {
                case Kind.Regular:
                    if (c == '\\')
                    {
                        BlankCode(i++);
                        BlankCode(i++);
                        continue;
                    }

                    if (c == '"' || c == '\n')
                    {
                        stack.Pop();
                        i += c == '"' ? 1 : 0;
                        continue;
                    }

                    break;
                case Kind.Verbatim:
                    if (c == '"')
                    {
                        if (At(i + 1) == '"')
                        {
                            BlankCode(i++);
                            BlankCode(i++);
                            continue;
                        }

                        stack.Pop();
                        i++;
                        continue;
                    }

                    break;
                default:
                    if (c == '"')
                    {
                        var run = 0;
                        while (At(i + run) == '"')
                        {
                            run++;
                        }

                        if (run >= frame.Quotes)
                        {
                            stack.Pop();
                            i += run;
                            continue;
                        }

                        for (var k = 0; k < run; k++)
                        {
                            BlankCode(i++);
                        }

                        continue;
                    }

                    break;
            }

            if (frame.Interpolated && c == '{')
            {
                var run = 0;
                while (At(i + run) == '{')
                {
                    run++;
                }

                var needed = frame.Kind == Kind.Raw ? frame.Dollars : 1;
                if (frame.Kind != Kind.Raw && run >= 2)
                {
                    BlankCode(i++); // "{{" is a literal brace.
                    BlankCode(i++);
                    continue;
                }

                if (run >= needed)
                {
                    for (var k = 0; k < run - needed; k++)
                    {
                        BlankCode(i++);
                    }

                    i += needed;
                    stack.Push(new Frame { Kind = Kind.Code });
                    continue;
                }

                for (var k = 0; k < run; k++)
                {
                    BlankCode(i++);
                }

                continue;
            }

            BlankCode(i++);
        }

        return (new string(code), new string(noComments));
    }

    /// <summary>1-based line number of <paramref name="index"/>.</summary>
    internal static int LineOf(string text, int index)
    {
        var line = 1;
        for (var k = 0; k < index && k < text.Length; k++)
        {
            if (text[k] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
