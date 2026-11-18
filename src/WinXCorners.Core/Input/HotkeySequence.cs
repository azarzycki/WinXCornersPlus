using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace WinXCorners.Core.Input;

/// <summary>
/// A fully validated custom hotkey such as <c>_control+k+control_+_control+_b</c>.
/// </summary>
/// <remarks>
/// Syntax (unchanged from the original app): tokens separated by <c>+</c>, surrounding whitespace ignored,
/// empty tokens ignored. <c>_key</c> holds a key down, <c>key_</c> releases it, a bare <c>key</c> presses and
/// releases it. Unlike the original, the whole text is validated before anything can be sent, so a typo in
/// the last token no longer fires the first half of the sequence.
/// </remarks>
public sealed class HotkeySequence
{
    /// <summary>Upper bound on tokens, so a pasted wall of text cannot keep the keyboard busy.</summary>
    public const int MaxSteps = 64;

    // Long hostile tokens are shortened in error messages so the Settings UI stays readable.
    private const int MaxTokenEcho = 32;

    private HotkeySequence(ImmutableArray<KeyStep> steps)
    {
        Steps = steps;
    }

    /// <summary>The steps in the order they are performed. Never empty.</summary>
    public IReadOnlyList<KeyStep> Steps { get; }

    /// <summary>
    /// Parses and validates <paramref name="text"/>. On failure <paramref name="error"/> names the offending
    /// token, its 1-based token number and its 1-based character column within <paramref name="text"/>.
    /// </summary>
    public static bool TryParse(
        string? text,
        [NotNullWhen(true)] out HotkeySequence? sequence,
        [NotNullWhen(false)] out string? error)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The hotkey is empty.";
            return false;
        }

        var steps = ImmutableArray.CreateBuilder<KeyStep>();
        var tokenNumber = 0;
        var segmentStart = 0;
        while (segmentStart <= text.Length)
        {
            var plus = text.IndexOf('+', segmentStart);
            var segmentEnd = plus < 0 ? text.Length : plus;

            // Locate the trimmed token inside the segment so the column we report is accurate.
            var start = segmentStart;
            var end = segmentEnd;
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }

            if (end > start)
            {
                tokenNumber++;
                if (tokenNumber > MaxSteps)
                {
                    error = $"The hotkey has more than {MaxSteps} keys.";
                    return false;
                }

                var token = text[start..end];
                if (!TryParseToken(token, out var step, out var problem))
                {
                    error = $"Key {tokenNumber} '{Shorten(token)}' (column {start + 1}): {problem}";
                    return false;
                }

                steps.Add(step);
            }

            if (plus < 0)
            {
                break;
            }

            segmentStart = plus + 1;
        }

        if (steps.Count == 0)
        {
            error = "The hotkey contains no keys.";
            return false;
        }

        sequence = new HotkeySequence(steps.ToImmutable());
        error = null;
        return true;
    }

    private static bool TryParseToken(string token, out KeyStep step, [NotNullWhen(false)] out string? problem)
    {
        step = default;
        var hold = token[0] == '_';
        var release = token.Length > 1 && token[^1] == '_';
        if (token.Length == 1 && hold)
        {
            problem = "'_' must be attached to a key name, as in _control or control_.";
            return false;
        }

        if (hold && release)
        {
            problem = "a key cannot be both held (_key) and released (key_) in one token.";
            return false;
        }

        var name = token[(hold ? 1 : 0)..(release ? token.Length - 1 : token.Length)];
        if (VirtualKeys.TryGet(name, out var virtualKey))
        {
            var kind = hold ? KeyStepKind.Hold : release ? KeyStepKind.Release : KeyStepKind.Press;
            step = new KeyStep(virtualKey, kind);
            problem = null;
            return true;
        }

        problem = VirtualKeys.IsMouseButtonName(name)
            ? $"'{name}' is a mouse button; hotkeys can only send keyboard keys."
            : $"unknown key name '{Shorten(name)}'.";
        return false;
    }

    private static string Shorten(string value) =>
        value.Length <= MaxTokenEcho ? value : string.Concat(value.AsSpan(0, MaxTokenEcho), "...");
}
