using System.Buffers;

namespace WinXCorners.Core.Input;

/// <summary>What a custom command's text asks the app to do.</summary>
public enum CustomCommandKind
{
    /// <summary>Blank text: nothing to do.</summary>
    Empty,

    /// <summary><c>!hotkeys</c>: send a hotkey sequence.</summary>
    HotkeySequence,

    /// <summary><c>#[...]</c> or <c>@[...]</c>: a conditional hotkey.</summary>
    ConditionalHotkey,

    /// <summary>Anything else: a program, document or URL to launch.</summary>
    Launch
}

/// <summary>
/// The result of <see cref="CustomCommandAnalyzer.Analyze"/>. When <see cref="IsValid"/> is true the payload
/// matching <see cref="Kind"/> is set (none for <see cref="CustomCommandKind.Empty"/>); when it is false,
/// <see cref="Error"/> explains why and no payload is set.
/// </summary>
public sealed record CustomCommandAnalysis(
    CustomCommandKind Kind,
    bool IsValid,
    string? Error,
    HotkeySequence? Sequence,
    ConditionalHotkeyExpression? Conditional,
    string? LaunchTarget);

/// <summary>
/// Classifies and validates a custom command's text. Used both to execute a command and to validate it
/// inline in the Settings dialog, so both always agree.
/// </summary>
public static class CustomCommandAnalyzer
{
    // Exactly the characters char.IsControl reports: C0 controls, DEL and C1 controls.
    private static readonly SearchValues<char> ControlCharacters = SearchValues.Create(
        Enumerable.Range(0, 0x20).Concat(Enumerable.Range(0x7F, 0x21)).Select(static c => (char)c).ToArray());

    /// <summary>Analyzes <paramref name="command"/> (leading and trailing whitespace ignored). Never throws.</summary>
    public static CustomCommandAnalysis Analyze(string? command)
    {
        var text = command?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return new CustomCommandAnalysis(CustomCommandKind.Empty, true, null, null, null, null);
        }

        switch (text[0])
        {
            case '!':
                return HotkeySequence.TryParse(text[1..], out var sequence, out var sequenceError)
                    ? new CustomCommandAnalysis(CustomCommandKind.HotkeySequence, true, null, sequence, null, null)
                    : Invalid(CustomCommandKind.HotkeySequence, sequenceError);
            case '#':
            case '@':
                return ConditionalHotkeyExpression.TryParse(text, out var conditional, out var conditionalError)
                    ? new CustomCommandAnalysis(CustomCommandKind.ConditionalHotkey, true, null, null, conditional, null)
                    : Invalid(CustomCommandKind.ConditionalHotkey, conditionalError);
        }

        // Control characters (newlines, tabs, NUL...) never belong in a path or URL and can hide what will
        // actually run from someone reading the setting.
        var controlIndex = text.AsSpan().IndexOfAny(ControlCharacters);
        return controlIndex < 0
            ? new CustomCommandAnalysis(CustomCommandKind.Launch, true, null, null, null, text)
            : Invalid(CustomCommandKind.Launch, $"The command contains a control character at column {controlIndex + 1}.");
    }

    private static CustomCommandAnalysis Invalid(CustomCommandKind kind, string error) =>
        new(kind, false, error, null, null, null);
}
