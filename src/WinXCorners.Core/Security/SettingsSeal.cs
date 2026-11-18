using System.Buffers;
using System.Text;
using System.Text.Json;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Security;

/// <summary>The security-relevant part of one custom command slot. Null strings are captured as empty.</summary>
public sealed record CommandSnapshot(string Name, string Command, string Parameters, bool LaunchHidden);

/// <summary>
/// Everything that decides what an elevated instance will do: which action each corner runs, whether custom
/// commands are on, what each of the <see cref="ApplicationSettings.CustomCommandCount"/> slots runs, and the
/// modifier key. Cosmetic settings and delays are deliberately excluded so that changing them never asks for
/// re-approval. Equality is by value, including the command list.
/// </summary>
/// <param name="TopLeftActionId">Unknown ids are captured as "none" because they never run anything.</param>
/// <param name="Commands">Always exactly <see cref="ApplicationSettings.CustomCommandCount"/> entries.</param>
public sealed record SecuritySnapshot(
    string TopLeftActionId,
    string TopRightActionId,
    string BottomLeftActionId,
    string BottomRightActionId,
    bool EnableCustomCommands,
    HotCornerModifierKey HotCornerModifierKey,
    IReadOnlyList<CommandSnapshot> Commands)
{
    public bool Equals(SecuritySnapshot? other)
    {
        return other is not null &&
               TopLeftActionId == other.TopLeftActionId &&
               TopRightActionId == other.TopRightActionId &&
               BottomLeftActionId == other.BottomLeftActionId &&
               BottomRightActionId == other.BottomRightActionId &&
               EnableCustomCommands == other.EnableCustomCommands &&
               HotCornerModifierKey == other.HotCornerModifierKey &&
               Commands.SequenceEqual(other.Commands);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(TopLeftActionId);
        hash.Add(TopRightActionId);
        hash.Add(BottomLeftActionId);
        hash.Add(BottomRightActionId);
        hash.Add(EnableCustomCommands);
        hash.Add(HotCornerModifierKey);
        foreach (var command in Commands)
        {
            hash.Add(command);
        }

        return hash.ToHashCode();
    }
}

public enum SealStatus
{
    /// <summary>No approval stored yet (first elevated run).</summary>
    NeverApproved,
    Unchanged,
    Changed,
    /// <summary>Something is stored but it is not a valid approval of a known version.</summary>
    Unreadable
}

/// <param name="Differences">Human-readable lines, one per changed item; empty unless Changed or Unreadable.</param>
public sealed record SealCheck(SealStatus Status, IReadOnlyList<string> Differences);

/// <summary>
/// The admin-only place the last approved snapshot is kept (the app implements it over HKLM, which normal
/// programs cannot write). <see cref="TryWrite"/> fails when not elevated.
/// </summary>
public interface IApprovedSettingsStore
{
    string? Read();

    bool TryWrite(string approval);
}

/// <summary>
/// The settings seal (brief design point 2). settings.json can be edited by any program running as the user, so an
/// elevated instance compares its settings with the approval it was last given and asks the user about any change
/// before acting on it.
/// </summary>
public static class SettingsSeal
{
    public const int FormatVersion = 1;

    private const string FormatName = "WinXCornersPlus.SettingsApproval";

    /// <summary>The line used when the stored approval exists but cannot be understood.</summary>
    public const string UnreadableDifference =
        "The stored approval could not be read, so all corner actions and commands need to be approved again.";

    private static readonly (string Label, Func<SecuritySnapshot, string> Get)[] Corners =
    [
        ("Top-left corner", static s => s.TopLeftActionId),
        ("Top-right corner", static s => s.TopRightActionId),
        ("Bottom-left corner", static s => s.BottomLeftActionId),
        ("Bottom-right corner", static s => s.BottomRightActionId)
    ];

    /// <summary>Projects the settings onto what matters for security. Safe on unsanitized input (null arrays, elements, strings).</summary>
    public static SecuritySnapshot Capture(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var source = settings.CustomCommands ?? [];
        var commands = Enumerable.Range(0, ApplicationSettings.CustomCommandCount)
            .Select(index => index < source.Length && source[index] is { } command
                ? new CommandSnapshot(command.Name ?? string.Empty, command.Command ?? string.Empty,
                    command.Parameters ?? string.Empty, command.LaunchHidden)
                : new CommandSnapshot(string.Empty, string.Empty, string.Empty, false))
            .ToArray();

        return new SecuritySnapshot(
            CanonicalActionId(settings.TopLeftActionId),
            CanonicalActionId(settings.TopRightActionId),
            CanonicalActionId(settings.BottomLeftActionId),
            CanonicalActionId(settings.BottomRightActionId),
            settings.EnableCustomCommands,
            settings.HotCornerModifierKey,
            commands);
    }

    /// <summary>
    /// The approval to store: compact JSON with a fixed property order and a format version, so the same settings
    /// always give the same string. It contains nothing outside <see cref="SecuritySnapshot"/>.
    /// </summary>
    public static string CreateApproval(ApplicationSettings settings) => Serialize(Capture(settings));

    /// <summary>Compares the current settings with a stored approval.</summary>
    public static SealCheck Check(ApplicationSettings current, string? storedApproval)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (string.IsNullOrWhiteSpace(storedApproval))
        {
            return new SealCheck(SealStatus.NeverApproved, []);
        }

        if (!TryDeserialize(storedApproval, out var approved))
        {
            return new SealCheck(SealStatus.Unreadable, [UnreadableDifference]);
        }

        var now = Capture(current);
        return now.Equals(approved)
            ? new SealCheck(SealStatus.Unchanged, [])
            : new SealCheck(SealStatus.Changed, Describe(approved, now));
    }

    /// <summary>
    /// What an elevated session would do with these settings, one line per corner and per configured command.
    /// Shown for first-time consent, where there is nothing to compare against.
    /// </summary>
    public static IReadOnlyList<string> Summarize(ApplicationSettings settings)
    {
        var snapshot = Capture(settings);
        var lines = Corners.Select(corner => $"{corner.Label}: {ActionLabel(snapshot, corner.Get(snapshot))}").ToList();
        lines.Add($"Custom commands: {OnOff(snapshot.EnableCustomCommands)}");
        lines.Add($"Modifier key: {ModifierName(snapshot.HotCornerModifierKey)}");
        for (var i = 0; i < snapshot.Commands.Count; i++)
        {
            var command = snapshot.Commands[i];
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                continue;
            }

            var hidden = command.LaunchHidden ? " (hidden window)" : string.Empty;
            lines.Add($"{SlotLabel(snapshot, i)} runs: {RunText(command)}{hidden}");
        }

        return lines;
    }

    private static IReadOnlyList<string> Describe(SecuritySnapshot was, SecuritySnapshot now)
    {
        var lines = new List<string>();
        foreach (var (label, get) in Corners)
        {
            if (get(was) != get(now))
            {
                lines.Add($"{label}: {ActionLabel(was, get(was))} -> {ActionLabel(now, get(now))}");
            }
        }

        if (was.EnableCustomCommands != now.EnableCustomCommands)
        {
            lines.Add($"Custom commands: {OnOff(was.EnableCustomCommands)} -> {OnOff(now.EnableCustomCommands)}");
        }

        if (was.HotCornerModifierKey != now.HotCornerModifierKey)
        {
            lines.Add($"Modifier key: {ModifierName(was.HotCornerModifierKey)} -> {ModifierName(now.HotCornerModifierKey)}");
        }

        for (var i = 0; i < ApplicationSettings.CustomCommandCount; i++)
        {
            var before = was.Commands[i];
            var after = now.Commands[i];
            var slot = $"Command {i + 1}";
            if (before.Name != after.Name)
            {
                lines.Add($"{slot} name: {Quoted(before.Name)} -> {Quoted(after.Name)}");
            }

            if (before.Command != after.Command || before.Parameters != after.Parameters)
            {
                lines.Add($"{slot} now runs: {RunText(after)} (was: {RunText(before)})");
            }

            if (before.LaunchHidden != after.LaunchHidden)
            {
                lines.Add($"{slot} window: {Visibility(before.LaunchHidden)} -> {Visibility(after.LaunchHidden)}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Name of an action for a prompt. Custom slots are always identified by their slot number, with the user's
    /// name in quotes after it, so a command named "Command 1" in slot 2 cannot pass itself off as slot 1.
    /// </summary>
    private static string ActionLabel(SecuritySnapshot snapshot, string actionId)
    {
        return ActionCatalog.TryGetCustomIndex(actionId, out var index)
            ? SlotLabel(snapshot, index)
            : ActionCatalog.GetDescriptiveName(ApplicationSettings.CreateDefault(), actionId);
    }

    private static string SlotLabel(SecuritySnapshot snapshot, int index)
    {
        var settings = new ApplicationSettings
        {
            CustomCommands = snapshot.Commands.Select(static c => new CustomCommandSettings { Name = c.Name }).ToArray()
        };
        var fallback = $"Command {index + 1}";
        var name = ActionCatalog.GetCustomCommandDisplayName(settings, index);
        return name == fallback ? fallback : $"{fallback} ({Quoted(name)})";
    }

    private static string RunText(CommandSnapshot command)
    {
        var text = (command.Command.Trim() + " " + command.Parameters.Trim()).Trim();
        return text.Length == 0 ? "nothing" : DisplayText.ForDisplay(text);
    }

    private static string Quoted(string value) => "\"" + DisplayText.ForDisplay(value) + "\"";

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Visibility(bool hidden) => hidden ? "hidden" : "shown";

    private static string ModifierName(HotCornerModifierKey key) =>
        Enum.IsDefined(key) ? key.ToString() : $"unknown value {(int)key}";

    private static string CanonicalActionId(string? id) => ActionCatalog.IsKnown(id) ? id! : ActionIds.None;

    private static string Serialize(SecuritySnapshot snapshot)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("format", FormatName);
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("topLeft", snapshot.TopLeftActionId);
            writer.WriteString("topRight", snapshot.TopRightActionId);
            writer.WriteString("bottomLeft", snapshot.BottomLeftActionId);
            writer.WriteString("bottomRight", snapshot.BottomRightActionId);
            writer.WriteBoolean("customCommands", snapshot.EnableCustomCommands);
            writer.WriteNumber("modifier", (int)snapshot.HotCornerModifierKey);
            writer.WriteStartArray("commands");
            foreach (var command in snapshot.Commands)
            {
                writer.WriteStartObject();
                writer.WriteString("name", command.Name);
                writer.WriteString("command", command.Command);
                writer.WriteString("parameters", command.Parameters);
                writer.WriteBoolean("hidden", command.LaunchHidden);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Strict reader: any missing, extra-typed or out-of-range field makes the approval unreadable.</summary>
    private static bool TryDeserialize(string text, out SecuritySnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "format", out var format) || format != FormatName ||
                !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
                !version.TryGetInt32(out var versionNumber) || versionNumber != FormatVersion ||
                !TryString(root, "topLeft", out var topLeft) ||
                !TryString(root, "topRight", out var topRight) ||
                !TryString(root, "bottomLeft", out var bottomLeft) ||
                !TryString(root, "bottomRight", out var bottomRight) ||
                !TryBool(root, "customCommands", out var customCommands) ||
                !root.TryGetProperty("modifier", out var modifier) || modifier.ValueKind != JsonValueKind.Number ||
                !modifier.TryGetInt32(out var modifierValue) ||
                !root.TryGetProperty("commands", out var commandsElement) ||
                commandsElement.ValueKind != JsonValueKind.Array ||
                commandsElement.GetArrayLength() != ApplicationSettings.CustomCommandCount)
            {
                return false;
            }

            var commands = new List<CommandSnapshot>();
            foreach (var element in commandsElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !TryString(element, "name", out var name) ||
                    !TryString(element, "command", out var command) ||
                    !TryString(element, "parameters", out var parameters) ||
                    !TryBool(element, "hidden", out var hidden))
                {
                    return false;
                }

                commands.Add(new CommandSnapshot(name, command, parameters, hidden));
            }

            snapshot = new SecuritySnapshot(topLeft, topRight, bottomLeft, bottomRight, customCommands,
                (HotCornerModifierKey)modifierValue, commands);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}

/// <summary>What an instance should do at start-up given its elevation and the seal state.</summary>
public enum ElevatedStartAction
{
    /// <summary>Not elevated: the seal does not apply.</summary>
    NotElevated,
    Proceed,
    /// <summary>First elevated run: show what will run (<see cref="SettingsSeal.Summarize"/>) and ask.</summary>
    AskFirstTimeConsent,
    /// <summary>Show <see cref="SealCheck.Differences"/> and ask before the elevated session uses them.</summary>
    AskToApproveChanges
}

public static class ElevatedStartPolicy
{
    /// <summary>Unreadable approvals are treated as changes; any unexpected status also asks (fail closed).</summary>
    public static ElevatedStartAction Decide(bool isElevated, SealCheck seal)
    {
        ArgumentNullException.ThrowIfNull(seal);
        if (!isElevated)
        {
            return ElevatedStartAction.NotElevated;
        }

        return seal.Status switch
        {
            SealStatus.Unchanged => ElevatedStartAction.Proceed,
            SealStatus.NeverApproved => ElevatedStartAction.AskFirstTimeConsent,
            _ => ElevatedStartAction.AskToApproveChanges
        };
    }
}
