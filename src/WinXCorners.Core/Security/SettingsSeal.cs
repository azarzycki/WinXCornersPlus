using System.Globalization;
using System.Text;
using System.Text.Json;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Security;

/// <summary>The security-relevant part of one custom command slot. Null strings are captured as empty.</summary>
public sealed record CommandSnapshot(string Name, string Command, string Parameters, bool LaunchHidden);

/// <summary>
/// Everything that decides what an elevated instance will do: which action each corner runs, whether custom
/// commands are on, what each of the <see cref="ApplicationSettings.CustomCommandCount"/> slots runs, the
/// modifier key, and whether the tray icon is hidden (an elevated session must never become invisible without
/// approval). Cosmetic settings and delays are deliberately excluded so that changing them never asks for
/// re-approval. Equality is by value (ordinal for strings), including the command list.
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
    bool AlwaysHideTrayIcon,
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
               AlwaysHideTrayIcon == other.AlwaysHideTrayIcon &&
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
        hash.Add(AlwaysHideTrayIcon);
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
/// <remarks>
/// Every settings-derived value in a prompt line (command names, programs, arguments) goes through
/// <see cref="DisplayText.Frame"/>: shown in full, never truncated, and unable to close its own frame, so the user
/// sees exactly what will run and where each value starts and ends.
/// </remarks>
public static class SettingsSeal
{
    /// <summary>
    /// Version 2 added the tray-icon switch and an encoding that keeps every string exact (version 1 silently
    /// turned unpaired surrogates into U+FFFD). Approvals of any other version are <see cref="SealStatus.Unreadable"/>.
    /// </summary>
    public const int FormatVersion = 2;

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
            settings.AlwaysHideTrayIcon,
            commands);
    }

    /// <summary>
    /// The approval to store: compact JSON with a fixed property order and a format version, so the same settings
    /// always give the same string. It is printable ASCII only, and the encoding is injective: two snapshots that
    /// differ in any way (even by an unpaired surrogate) never give the same approval. It contains nothing outside
    /// <see cref="SecuritySnapshot"/>.
    /// </summary>
    public static string CreateApproval(ApplicationSettings settings) => Serialize(Capture(settings));

    /// <summary>
    /// Compares the current settings with a stored approval. Never throws for any stored text: anything that is
    /// not exactly what <see cref="CreateApproval"/> produces for some snapshot is <see cref="SealStatus.Unreadable"/>.
    /// </summary>
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
    /// What an elevated session would do with these settings, one line per corner, per switch and per configured
    /// command. Shown for first-time consent, where there is nothing to compare against. Never truncated.
    /// </summary>
    public static IReadOnlyList<string> Summarize(ApplicationSettings settings)
    {
        var snapshot = Capture(settings);
        var lines = Corners.Select(corner => $"{corner.Label}: {ActionLabel(snapshot, corner.Get(snapshot))}").ToList();
        lines.Add($"Custom commands: {OnOff(snapshot.EnableCustomCommands)}");
        lines.Add($"Modifier key: {ModifierName(snapshot.HotCornerModifierKey)}");
        lines.Add($"Tray icon: {Visibility(snapshot.AlwaysHideTrayIcon)}");
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

        if (was.AlwaysHideTrayIcon != now.AlwaysHideTrayIcon)
        {
            lines.Add($"Tray icon: {Visibility(was.AlwaysHideTrayIcon)} -> {Visibility(now.AlwaysHideTrayIcon)}");
        }

        for (var i = 0; i < ApplicationSettings.CustomCommandCount; i++)
        {
            var before = was.Commands[i];
            var after = now.Commands[i];
            var slot = $"Command {i + 1}";
            if (before.Name != after.Name)
            {
                lines.Add($"{slot} name: {DisplayText.Frame(before.Name)} -> {DisplayText.Frame(after.Name)}");
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
    /// name framed after it, so a command named "Command 1" in slot 2 cannot pass itself off as slot 1.
    /// </summary>
    private static string ActionLabel(SecuritySnapshot snapshot, string actionId)
    {
        return ActionCatalog.TryGetCustomIndex(actionId, out var index)
            ? SlotLabel(snapshot, index)
            : ActionCatalog.GetDescriptiveName(ApplicationSettings.CreateDefault(), actionId);
    }

    /// <summary>"Command N", plus the exact name in a frame unless the name is blank.</summary>
    private static string SlotLabel(SecuritySnapshot snapshot, int index)
    {
        var slot = $"Command {index + 1}";
        var name = snapshot.Commands[index].Name;
        return string.IsNullOrWhiteSpace(name) ? slot : $"{slot} ({DisplayText.Frame(name)})";
    }

    /// <summary>
    /// The program and its arguments as two separately framed fields, exactly as stored, so that moving text
    /// between them (<c>C:\Program</c> + <c>Files\x.exe</c>) is always visible.
    /// </summary>
    private static string RunText(CommandSnapshot command)
    {
        if (command.Command.Length == 0 && command.Parameters.Length == 0)
        {
            return "nothing";
        }

        var program = "program " + DisplayText.Frame(command.Command);
        return command.Parameters.Length == 0 ? program : $"{program} with arguments {DisplayText.Frame(command.Parameters)}";
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string Visibility(bool hidden) => hidden ? "hidden" : "shown";

    private static string ModifierName(HotCornerModifierKey key) =>
        Enum.IsDefined(key) ? key.ToString() : $"unknown value {(int)key}";

    private static string CanonicalActionId(string? id) => ActionCatalog.IsKnown(id) ? id! : ActionIds.None;

    private static string Serialize(SecuritySnapshot snapshot)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendString(builder, "format", FormatName).Append(',');
        AppendRaw(builder, "version", FormatVersion.ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendString(builder, "topLeft", snapshot.TopLeftActionId).Append(',');
        AppendString(builder, "topRight", snapshot.TopRightActionId).Append(',');
        AppendString(builder, "bottomLeft", snapshot.BottomLeftActionId).Append(',');
        AppendString(builder, "bottomRight", snapshot.BottomRightActionId).Append(',');
        AppendRaw(builder, "customCommands", Bool(snapshot.EnableCustomCommands)).Append(',');
        AppendRaw(builder, "modifier", ((int)snapshot.HotCornerModifierKey).ToString(CultureInfo.InvariantCulture)).Append(',');
        AppendRaw(builder, "hideTrayIcon", Bool(snapshot.AlwaysHideTrayIcon)).Append(',');
        builder.Append("\"commands\":[");
        for (var i = 0; i < snapshot.Commands.Count; i++)
        {
            var command = snapshot.Commands[i];
            builder.Append(i == 0 ? "{" : ",{");
            AppendString(builder, "name", command.Name).Append(',');
            AppendString(builder, "command", command.Command).Append(',');
            AppendString(builder, "parameters", command.Parameters).Append(',');
            AppendRaw(builder, "hidden", Bool(command.LaunchHidden)).Append('}');
        }

        return builder.Append("]}").ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static StringBuilder AppendRaw(StringBuilder builder, string name, string value) =>
        builder.Append('"').Append(name).Append("\":").Append(value);

    /// <summary>
    /// Writes a JSON string in which every UTF-16 code unit outside printable ASCII, and <c>"</c> and <c>\</c>,
    /// is a <c>\uXXXX</c> escape. Unlike <see cref="Utf8JsonWriter"/> this keeps unpaired surrogates as they are,
    /// so the encoding is injective over all .NET strings.
    /// </summary>
    private static StringBuilder AppendString(StringBuilder builder, string name, string value)
    {
        builder.Append('"').Append(name).Append("\":\"");
        foreach (var c in value)
        {
            if (c is >= ' ' and <= '~' and not '"' and not '\\')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(@"\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return builder.Append('"');
    }

    /// <summary>
    /// Strict reader. It accepts only text that is exactly what <see cref="Serialize"/> writes for the snapshot it
    /// describes: after parsing, the snapshot is written again and must match character for character. That
    /// rejects duplicate keys, reordered or extra fields, whitespace and alternative escapes, so the reader can
    /// never accept an approval the writer could not have produced.
    /// </summary>
    private static bool TryDeserialize(string text, out SecuritySnapshot snapshot)
    {
        snapshot = null!;

        // The writer emits printable ASCII only. Checking first also keeps raw unpaired surrogates (which the
        // JSON parser cannot transcode) away from the parser.
        if (text.AsSpan().ContainsAnyExceptInRange(' ', '~'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "format", out var format) || format != FormatName ||
                !TryInt(root, "version", out var version) || version != FormatVersion ||
                !TryString(root, "topLeft", out var topLeft) ||
                !TryString(root, "topRight", out var topRight) ||
                !TryString(root, "bottomLeft", out var bottomLeft) ||
                !TryString(root, "bottomRight", out var bottomRight) ||
                !TryBool(root, "customCommands", out var customCommands) ||
                !TryInt(root, "modifier", out var modifier) ||
                !TryBool(root, "hideTrayIcon", out var hideTrayIcon) ||
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
                (HotCornerModifierKey)modifier, hideTrayIcon, commands);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: the property lookup unescapes names, and an escaped unpaired surrogate in
            // a name (which the writer never produces) makes it throw.
            return false;
        }

        return string.Equals(Serialize(snapshot), text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a string property from its raw JSON text, so that <c>\uD800</c> (an unpaired surrogate the writer
    /// may legitimately produce) decodes to that code unit instead of making the JSON library throw. Only the
    /// <c>\uXXXX</c> escapes the writer uses are accepted.
    /// </summary>
    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = property.GetRawText();
        var builder = new StringBuilder(raw.Length);
        for (var i = 1; i < raw.Length - 1; i++)
        {
            if (raw[i] != '\\')
            {
                builder.Append(raw[i]);
                continue;
            }

            // The parser has already checked that an escape is complete; \uXXXX always has four hex digits.
            if (raw[i + 1] != 'u')
            {
                return false;
            }

            builder.Append((char)ushort.Parse(raw.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            i += 5;
        }

        value = builder.ToString();
        return true;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt32(out value);
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
