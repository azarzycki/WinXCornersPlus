using WinXCorners.Core.Model;

namespace WinXCorners.Core.Actions;

public enum ActionKind
{
    None,
    BuiltIn,
    Custom
}

/// <summary>Stable identifiers written to settings.json. Never rename an existing value.</summary>
public static class ActionIds
{
    public const string None = "none";
    public const string FileExplorer = "file-explorer";
    public const string Settings = "settings";
    public const string TaskManager = "task-manager";
    public const string AllWindows = "all-windows";
    public const string Desktop = "desktop";
    public const string ScreenSaver = "screen-saver";
    public const string MonitorsOff = "monitors-off";
    public const string ActionCenter = "action-center";
    public const string NotificationCenter = "notification-center";
    public const string LockScreen = "lock-screen";
    public const string StartMenu = "start-menu";
    public const string HideOtherWindows = "hide-other-windows";
    public const string CustomPrefix = "custom-";
}

/// <param name="Id">Value persisted in settings.json.</param>
/// <param name="MenuText">Label in the corner picker menu.</param>
/// <param name="ButtonText">Shorter label shown on the flyout button once selected.</param>
/// <param name="CustomIndex">Zero-based custom command slot, or -1.</param>
public sealed record ActionDefinition(string Id, string MenuText, string ButtonText, ActionKind Kind, int CustomIndex = -1);

/// <summary>A row in the corner picker menu. <see cref="IsSeparator"/> rows have no action.</summary>
public readonly record struct CornerMenuItem(string Text, string ActionId, bool IsSeparator = false)
{
    public static CornerMenuItem Separator { get; } = new(string.Empty, string.Empty, IsSeparator: true);
}

/// <summary>
/// The single source of truth for which corner actions exist and how they are labelled.
/// Everything else (menus, button labels, validation, execution) keys off this table.
/// </summary>
public static class ActionCatalog
{
    public static ActionDefinition None { get; } = new(ActionIds.None, "None", string.Empty, ActionKind.None);

    public static IReadOnlyList<ActionDefinition> BuiltIn { get; } =
    [
        new(ActionIds.FileExplorer, "File Explorer", "File Explorer", ActionKind.BuiltIn),
        new(ActionIds.Settings, "System Settings", "System Settings", ActionKind.BuiltIn),
        new(ActionIds.TaskManager, "Task Manager", "Task Manager", ActionKind.BuiltIn),
        new(ActionIds.AllWindows, "Show All Windows", "All Windows", ActionKind.BuiltIn),
        new(ActionIds.Desktop, "Show Desktop", "Show Desktop", ActionKind.BuiltIn),
        new(ActionIds.ScreenSaver, "Start Screen Saver", "Screen Saver", ActionKind.BuiltIn),
        new(ActionIds.MonitorsOff, "Turn Display Off", "Display Off", ActionKind.BuiltIn),
        new(ActionIds.ActionCenter, "Action Center", "Action Center", ActionKind.BuiltIn),
        new(ActionIds.NotificationCenter, "Notification Center", "Notification Center", ActionKind.BuiltIn),
        new(ActionIds.LockScreen, "Lock Screen", "Lock Screen", ActionKind.BuiltIn),
        new(ActionIds.StartMenu, "Start Menu", "Start Menu", ActionKind.BuiltIn),
        new(ActionIds.HideOtherWindows, "Hide Other Windows", "Hide Windows", ActionKind.BuiltIn)
    ];

    private static readonly Dictionary<string, ActionDefinition> ById = BuildIndex();

    public static string CustomId(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ApplicationSettings.CustomCommandCount);
        return ActionIds.CustomPrefix + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>True for "none", every built-in id and custom-1..custom-N. Comparison is ordinal and case-sensitive.</summary>
    public static bool IsKnown(string? actionId) => actionId is not null && ById.ContainsKey(actionId);

    public static bool TryGet(string? actionId, out ActionDefinition definition)
    {
        if (actionId is not null && ById.TryGetValue(actionId, out var found))
        {
            definition = found;
            return true;
        }

        definition = None;
        return false;
    }

    public static bool TryGetCustomIndex(string? actionId, out int index)
    {
        if (TryGet(actionId, out var definition) && definition.Kind == ActionKind.Custom)
        {
            index = definition.CustomIndex;
            return true;
        }

        index = -1;
        return false;
    }

    /// <summary>True when the id is missing, blank, unknown, or "none": nothing should run.</summary>
    public static bool IsNoOp(string? actionId)
    {
        return !TryGet(actionId, out var definition) || definition.Kind == ActionKind.None;
    }

    /// <summary>
    /// The user-visible name of a custom command slot: the trimmed name they typed, or "Command N"
    /// when it is blank or is just one of the stock placeholders.
    /// </summary>
    public static string GetCustomCommandDisplayName(ApplicationSettings settings, int index)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fallback = $"Command {index + 1}";
        var commands = settings.CustomCommands;
        if (commands is null || index < 0 || index >= commands.Length)
        {
            return fallback;
        }

        var name = commands[index]?.Name?.Trim();
        if (string.IsNullOrEmpty(name) ||
            string.Equals(name, fallback, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, $"Custom Command {index + 1}", StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        return name;
    }

    /// <summary>Label for the flyout button of a corner. Unknown ids render as empty rather than leaking raw text.</summary>
    public static string GetButtonText(ApplicationSettings settings, string? actionId)
    {
        if (!TryGet(actionId, out var definition))
        {
            return string.Empty;
        }

        return definition.Kind == ActionKind.Custom
            ? GetCustomCommandDisplayName(settings, definition.CustomIndex)
            : definition.ButtonText;
    }

    /// <summary>Name used in logs and prompts: never empty ("None" for the no-op).</summary>
    public static string GetDescriptiveName(ApplicationSettings settings, string? actionId)
    {
        if (!TryGet(actionId, out var definition))
        {
            return "None";
        }

        return definition.Kind == ActionKind.Custom
            ? GetCustomCommandDisplayName(settings, definition.CustomIndex)
            : definition.MenuText;
    }

    /// <summary>
    /// Rows for the corner picker: "None", the built-ins sorted by label, then (only when custom
    /// commands are enabled) a separator and the four custom slots sorted by label.
    /// </summary>
    public static IReadOnlyList<CornerMenuItem> BuildCornerMenu(ApplicationSettings settings, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        comparer ??= StringComparer.CurrentCultureIgnoreCase;

        var items = new List<CornerMenuItem> { new(None.MenuText, None.Id) };
        items.AddRange(BuiltIn
            .OrderBy(static definition => definition.MenuText, comparer)
            .Select(static definition => new CornerMenuItem(definition.MenuText, definition.Id)));

        if (settings.EnableCustomCommands)
        {
            items.Add(CornerMenuItem.Separator);
            items.AddRange(Enumerable.Range(0, ApplicationSettings.CustomCommandCount)
                .Select(index => new CornerMenuItem(GetCustomCommandDisplayName(settings, index), CustomId(index)))
                .OrderBy(static item => item.Text, comparer));
        }

        return items;
    }

    private static Dictionary<string, ActionDefinition> BuildIndex()
    {
        var index = new Dictionary<string, ActionDefinition>(StringComparer.Ordinal) { [None.Id] = None };
        foreach (var definition in BuiltIn)
        {
            index.Add(definition.Id, definition);
        }

        for (var slot = 0; slot < ApplicationSettings.CustomCommandCount; slot++)
        {
            var id = CustomId(slot);
            index.Add(id, new ActionDefinition(id, $"Command {slot + 1}", $"Command {slot + 1}", ActionKind.Custom, slot));
        }

        return index;
    }
}
