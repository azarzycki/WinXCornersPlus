using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace WinXCorners.Core.Input;

/// <summary>
/// A conditional hotkey such as <c>#[Chrome_WidgetWin_1]:(_control+k+control_+_control+_b)?(win)</c>.
/// </summary>
/// <remarks>
/// <para>Grammar: one or more conditions, then <c>:(</c>hotkeys<c>)</c>, then optionally <c>?(</c>hotkeys<c>)</c>.
/// Whitespace is allowed between these elements and nothing may follow the last one. A condition is
/// <c>#[class]</c>, <c>#[class,title]</c> or <c>#[,title]</c> (<c>#</c> = foreground window), or the same with
/// <c>@</c> (any open window). The class and title are trimmed; the title runs to the closing <c>]</c> and may
/// contain commas but not <c>]</c>.</para>
/// <para>Replaces the regex-based parser (review B9), which ran the else-branch when no condition parsed,
/// found <c>?(...)</c> anywhere in the text (including inside a title), and sent half-valid hotkeys.</para>
/// </remarks>
public sealed class ConditionalHotkeyExpression
{
    private const int MaxEcho = 24;

    private ConditionalHotkeyExpression(ImmutableArray<WindowCondition> conditions, HotkeySequence whenMatched, HotkeySequence? otherwise)
    {
        Conditions = conditions;
        WhenMatched = whenMatched;
        Otherwise = otherwise;
    }

    /// <summary>The conditions, OR-ed together. Never empty.</summary>
    public IReadOnlyList<WindowCondition> Conditions { get; }

    /// <summary>Sent when any condition matches.</summary>
    public HotkeySequence WhenMatched { get; }

    /// <summary>Sent when no condition matches; null when the expression has no <c>?(...)</c> branch.</summary>
    public HotkeySequence? Otherwise { get; }

    /// <summary>
    /// Parses and validates the whole expression, including both hotkey bodies. Errors give a 1-based column.
    /// </summary>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out ConditionalHotkeyExpression? expression,
        [NotNullWhen(false)] out string? error)
    {
        expression = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The conditional hotkey is empty.";
            return false;
        }

        var position = SkipWhiteSpace(text, 0);
        var conditions = ImmutableArray.CreateBuilder<WindowCondition>();
        while (position < text.Length && text[position] is '#' or '@')
        {
            if (!TryParseCondition(text, ref position, out var condition, out error))
            {
                return false;
            }

            conditions.Add(condition);
            position = SkipWhiteSpace(text, position);
        }

        if (conditions.Count == 0)
        {
            error = $"Expected a window condition such as #[class] or @[class,title] {Describe(text, position)}.";
            return false;
        }

        if (position >= text.Length || text[position] != ':')
        {
            error = $"Expected ':' after the window conditions {Describe(text, position)}.";
            return false;
        }

        position = SkipWhiteSpace(text, position + 1);
        if (!TryParseBody(text, ref position, "':'", out var whenMatched, out error))
        {
            return false;
        }

        position = SkipWhiteSpace(text, position);
        HotkeySequence? otherwise = null;
        if (position < text.Length && text[position] == '?')
        {
            position = SkipWhiteSpace(text, position + 1);
            if (!TryParseBody(text, ref position, "'?'", out otherwise, out error))
            {
                return false;
            }

            position = SkipWhiteSpace(text, position);
        }

        if (position < text.Length)
        {
            error = $"Unexpected text {Describe(text, position)}; nothing may follow the hotkeys.";
            return false;
        }

        expression = new ConditionalHotkeyExpression(conditions.ToImmutable(), whenMatched, otherwise);
        error = null;
        return true;
    }

    /// <summary>True when any condition matches the current windows.</summary>
    public bool IsMatch(IWindowQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The foreground window is read at most once so all '#' conditions see the same window.
        var foregroundRead = false;
        WindowInfo? foreground = null;
        foreach (var condition in Conditions)
        {
            if (condition.Scope == WindowConditionScope.Foreground)
            {
                if (!foregroundRead)
                {
                    foreground = query.GetForegroundWindow();
                    foregroundRead = true;
                }

                if (foreground is { } window && MatchesForeground(condition, window))
                {
                    return true;
                }
            }
            else if (query.WindowExists(NullIfEmpty(condition.ClassName), NullIfEmpty(condition.TitleText)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The sequence to send now: <see cref="WhenMatched"/>, <see cref="Otherwise"/>, or null for nothing.</summary>
    public HotkeySequence? Select(IWindowQuery query) => IsMatch(query) ? WhenMatched : Otherwise;

    private static bool MatchesForeground(WindowCondition condition, WindowInfo window)
    {
        var classMatches = condition.ClassName.Length == 0 ||
                           string.Equals(window.ClassName, condition.ClassName, StringComparison.OrdinalIgnoreCase);
        var titleMatches = condition.TitleText.Length == 0 ||
                           (window.Title ?? string.Empty).Contains(condition.TitleText, StringComparison.OrdinalIgnoreCase);
        return classMatches && titleMatches;
    }

    // FindWindow treats "" as "a window whose class/title is empty", not as a wildcard (review B9).
    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static bool TryParseCondition(string text, ref int position, [NotNullWhen(true)] out WindowCondition? condition, [NotNullWhen(false)] out string? error)
    {
        condition = null;
        var marker = text[position];
        var scope = marker == '#' ? WindowConditionScope.Foreground : WindowConditionScope.AnyWindow;
        var markerColumn = position + 1;
        position++;
        if (position >= text.Length || text[position] != '[')
        {
            error = $"Expected '[' directly after '{marker}' (column {markerColumn}).";
            return false;
        }

        var close = text.IndexOf(']', position + 1);
        if (close < 0)
        {
            error = $"Missing ']' for the condition starting at column {markerColumn}.";
            return false;
        }

        var content = text[(position + 1)..close];
        var comma = content.IndexOf(',');
        var className = (comma < 0 ? content : content[..comma]).Trim();
        var title = comma < 0 ? string.Empty : content[(comma + 1)..].Trim();
        if (className.Length == 0 && title.Length == 0)
        {
            error = $"The condition at column {markerColumn} needs a window class, a title, or both.";
            return false;
        }

        position = close + 1;
        condition = new WindowCondition(scope, className, title);
        error = null;
        return true;
    }

    private static bool TryParseBody(string text, ref int position, string after, [NotNullWhen(true)] out HotkeySequence? sequence, [NotNullWhen(false)] out string? error)
    {
        sequence = null;
        if (position >= text.Length || text[position] != '(')
        {
            error = $"Expected '(' after {after} {Describe(text, position)}.";
            return false;
        }

        var bodyStart = position + 1;
        var close = text.IndexOfAny(['(', ')'], bodyStart);
        if (close < 0)
        {
            error = $"Missing ')' for the '(' at column {bodyStart}.";
            return false;
        }

        if (text[close] == '(')
        {
            error = $"Nested '(' at column {close + 1}; hotkeys cannot contain parentheses.";
            return false;
        }

        if (!HotkeySequence.TryParse(text[bodyStart..close], out sequence, out var inner))
        {
            error = $"In the hotkeys after {after} (starting at column {bodyStart + 1}): {inner}";
            return false;
        }

        position = close + 1;
        error = null;
        return true;
    }

    private static int SkipWhiteSpace(string text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static string Describe(string text, int position)
    {
        if (position >= text.Length)
        {
            return "at the end of the text";
        }

        var rest = text[position..];
        var echo = rest.Length <= MaxEcho ? rest : string.Concat(rest.AsSpan(0, MaxEcho), "...");
        return $"at column {position + 1} ('{echo}')";
    }
}
