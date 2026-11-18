using System.Globalization;
using System.Text;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Settings;

/// <summary>
/// Turns any <see cref="ApplicationSettings"/> instance (typically one just deserialized from an
/// untrusted settings.json) into one the rest of the app can use without further checks.
/// </summary>
public static class SettingsSanitizer
{
    /// <summary>Longest custom command name kept; longer names are truncated.</summary>
    public const int MaxNameLength = 32;

    /// <summary>Longest custom command target kept; longer values are truncated.</summary>
    public const int MaxCommandLength = 2048;

    /// <summary>Longest custom command parameter string kept; longer values are truncated.</summary>
    public const int MaxParametersLength = 4096;

    // Corrections quote the offending value; keep that short so a hostile file cannot flood a log or dialog.
    private const int MaxQuotedValueLength = 40;

    /// <summary>
    /// Repairs <paramref name="settings"/> in place and returns one human-readable line per correction made
    /// (empty when the input was already clean). Idempotent: a second call always returns an empty list.
    /// </summary>
    /// <remarks>
    /// Unknown action ids become "none" rather than a default: an id we do not recognise might have meant
    /// anything, and silently running a different action is worse than running nothing.
    /// </remarks>
    public static IReadOnlyList<string> Sanitize(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var corrections = new List<string>();

        settings.TopLeftActionId = SanitizeActionId(settings.TopLeftActionId, ApplicationSettings.DefaultTopLeftActionId, nameof(ApplicationSettings.TopLeftActionId), corrections);
        settings.TopRightActionId = SanitizeActionId(settings.TopRightActionId, ApplicationSettings.DefaultTopRightActionId, nameof(ApplicationSettings.TopRightActionId), corrections);
        settings.BottomLeftActionId = SanitizeActionId(settings.BottomLeftActionId, ApplicationSettings.DefaultBottomLeftActionId, nameof(ApplicationSettings.BottomLeftActionId), corrections);
        settings.BottomRightActionId = SanitizeActionId(settings.BottomRightActionId, ApplicationSettings.DefaultBottomRightActionId, nameof(ApplicationSettings.BottomRightActionId), corrections);

        settings.GlobalDelayIndex = SanitizeDelayIndex(settings.GlobalDelayIndex, nameof(ApplicationSettings.GlobalDelayIndex), corrections);
        settings.TopLeftDelayIndex = SanitizeDelayIndex(settings.TopLeftDelayIndex, nameof(ApplicationSettings.TopLeftDelayIndex), corrections);
        settings.TopRightDelayIndex = SanitizeDelayIndex(settings.TopRightDelayIndex, nameof(ApplicationSettings.TopRightDelayIndex), corrections);
        settings.BottomLeftDelayIndex = SanitizeDelayIndex(settings.BottomLeftDelayIndex, nameof(ApplicationSettings.BottomLeftDelayIndex), corrections);
        settings.BottomRightDelayIndex = SanitizeDelayIndex(settings.BottomRightDelayIndex, nameof(ApplicationSettings.BottomRightDelayIndex), corrections);

        if (!Enum.IsDefined(settings.FlyoutAnimationDirection))
        {
            corrections.Add(Invariant($"FlyoutAnimationDirection {(int)settings.FlyoutAnimationDirection} is not defined; reset to {FlyoutAnimationDirection.Bottom}."));
            settings.FlyoutAnimationDirection = FlyoutAnimationDirection.Bottom;
        }

        if (!Enum.IsDefined(settings.HotCornerModifierKey))
        {
            corrections.Add(Invariant($"HotCornerModifierKey {(int)settings.HotCornerModifierKey} is not defined; reset to {HotCornerModifierKey.None}."));
            settings.HotCornerModifierKey = HotCornerModifierKey.None;
        }

        SanitizeCustomCommands(settings, corrections);
        return corrections;
    }

    private static string SanitizeActionId(string? value, string defaultId, string propertyName, List<string> corrections)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            corrections.Add($"{propertyName} was empty; reset to \"{defaultId}\".");
            return defaultId;
        }

        if (!ActionCatalog.IsKnown(value))
        {
            corrections.Add($"{propertyName} {Quote(value)} is not a known action; set to \"{ActionIds.None}\".");
            return ActionIds.None;
        }

        return value;
    }

    private static int SanitizeDelayIndex(int value, string propertyName, List<string> corrections)
    {
        if (value is >= 0 and < ApplicationSettings.DelayOptionCount)
        {
            return value;
        }

        corrections.Add(Invariant($"{propertyName} {value} is outside 0..{ApplicationSettings.DelayOptionCount - 1}; reset to {ApplicationSettings.DefaultDelayIndex}."));
        return ApplicationSettings.DefaultDelayIndex;
    }

    private static void SanitizeCustomCommands(ApplicationSettings settings, List<string> corrections)
    {
        var commands = settings.CustomCommands;
        if (commands is null)
        {
            corrections.Add("CustomCommands was missing; reset to empty commands.");
            settings.CustomCommands = ApplicationSettings.CreateDefaultCommands();
            return;
        }

        if (commands.Length != ApplicationSettings.CustomCommandCount)
        {
            corrections.Add(Invariant($"CustomCommands had {commands.Length} entries; adjusted to {ApplicationSettings.CustomCommandCount}."));
            var resized = ApplicationSettings.CreateDefaultCommands();
            Array.Copy(commands, resized, Math.Min(commands.Length, resized.Length));
            settings.CustomCommands = resized;
            commands = resized;
        }

        for (var index = 0; index < commands.Length; index++)
        {
            var label = Invariant($"CustomCommands[{index}]");
            var command = commands[index];
            if (command is null)
            {
                corrections.Add($"{label} was null; replaced with an empty command.");
                commands[index] = new CustomCommandSettings();
                continue;
            }

            command.Name = SanitizeText(command.Name, MaxNameLength, trim: true, $"{label}.Name", corrections);
            command.Command = SanitizeText(command.Command, MaxCommandLength, trim: false, $"{label}.Command", corrections);
            command.Parameters = SanitizeText(command.Parameters, MaxParametersLength, trim: false, $"{label}.Parameters", corrections);
        }
    }

    private static string SanitizeText(string? value, int maxLength, bool trim, string label, List<string> corrections)
    {
        if (value is null)
        {
            corrections.Add($"{label} was null; set to empty.");
            return string.Empty;
        }

        var result = RemoveControlCharacters(value);
        if (result.Length != value.Length)
        {
            corrections.Add($"{label}: removed {value.Length - result.Length} control character(s).");
        }

        if (trim)
        {
            var trimmed = result.Trim();
            if (trimmed.Length != result.Length)
            {
                corrections.Add($"{label}: removed surrounding whitespace.");
            }

            result = trimmed;
        }

        if (result.Length > maxLength)
        {
            corrections.Add(Invariant($"{label} was {result.Length} characters; truncated to {maxLength}."));
            result = Truncate(result, maxLength);
            if (trim)
            {
                // Truncation can expose trailing whitespace; strip it now so a second pass finds nothing to do.
                result = result.TrimEnd();
            }
        }

        return result;
    }

    /// <summary>Removes Unicode category Cc characters (C0, DEL, C1): none can be typed into the settings UI.</summary>
    private static string RemoveControlCharacters(string value)
    {
        if (!value.Any(char.IsControl))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>Cuts to at most <paramref name="maxLength"/> UTF-16 units without splitting a surrogate pair.</summary>
    private static string Truncate(string value, int maxLength)
    {
        var length = maxLength;
        if (char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return value[..length];
    }

    private static string Quote(string value)
    {
        var shown = RemoveControlCharacters(value);
        if (shown.Length > MaxQuotedValueLength)
        {
            shown = Truncate(shown, MaxQuotedValueLength) + "...";
        }

        return "\"" + shown + "\"";
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
