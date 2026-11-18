using System.Globalization;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Security;

/// <summary>Ordered from least to most serious; lists are sorted with the highest value first.</summary>
public enum RiskSeverity
{
    Info,
    Caution,
    Warning
}

/// <param name="Id">Stable kebab-case key; the UI and tests rely on it. Never rename an existing id.</param>
/// <param name="Title">At most <see cref="SecurityPosture.MaxTitleLength"/> characters.</param>
/// <param name="Detail">At most <see cref="SecurityPosture.MaxDetailLength"/> characters.</param>
public sealed record RiskItem(string Id, RiskSeverity Severity, string Title, string Detail);

/// <summary>Facts the app gathers about the running process.</summary>
public sealed record SecurityContext(bool IsElevated, InstallLocationAssessment Install, bool StartsWithWindows, string SettingsPath);

/// <summary>
/// All the security text the user sees (brief design point 5), generated from the current facts so it can be
/// tested and never drifts from what the app actually does.
/// </summary>
public static class SecurityPosture
{
    public const int MaxTitleLength = 60;
    public const int MaxDetailLength = 320;

    /// <summary>Stable ids of every item this class can produce.</summary>
    public static class Ids
    {
        public const string RunningElevated = "running-elevated";
        public const string ElevatedFromUserWritableFolder = "elevated-from-user-writable-folder";
        public const string AlwaysRunAsAdministrator = "always-run-as-administrator";
        public const string CustomCommandsEnabled = "custom-commands-enabled";
        public const string CommandLaunchesHidden = "command-launches-hidden";
        public const string TrayIconHidden = "tray-icon-hidden";
        public const string RunsAtStartup = "runs-at-startup";

        public const string ElevationControlsElevatedWindows = "elevation-controls-elevated-windows";
        public const string ElevationLaunchesWithoutAdmin = "elevation-launches-without-admin";
        public const string ElevationSettingsApproval = "elevation-settings-approval";
        public const string ElevationUserWritableInstall = "elevation-user-writable-install";
        public const string ElevationPromptEveryStart = "elevation-prompt-every-start";
    }

    /// <summary>The name <see cref="ForSetting"/> accepts for a custom command's "launch hidden" switch.</summary>
    public const string LaunchHiddenSetting = nameof(CustomCommandSettings.LaunchHidden);

    /// <summary>The live posture for the Security page: one item per risk that is currently true, most severe first.</summary>
    public static IReadOnlyList<RiskItem> Evaluate(ApplicationSettings settings, SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        var installProtected = IsProtected(context.Install);
        var items = new List<RiskItem>();

        if (context.IsElevated && !installProtected)
        {
            items.Add(ElevatedFromUserWritableFolder());
        }

        if (context.IsElevated)
        {
            // With "Always run as administrator" on, a normal start elevates again, so that switch has to go first.
            var howToStop = settings.AlwaysRunAsAdministrator
                ? "To stop, turn off Always run as administrator, then exit the app and start it again."
                : "To stop, exit the app and start it normally.";
            items.Add(new RiskItem(Ids.RunningElevated, RiskSeverity.Caution, "Running as administrator",
                "This session has administrator rights, so its corner and hotkey actions can control elevated windows. " +
                "Programs and links it opens still start without administrator rights. " + howToStop));
        }

        if (settings.AlwaysRunAsAdministrator)
        {
            items.Add(AlwaysRunAsAdministrator(installProtected));
        }

        if (settings.EnableCustomCommands)
        {
            items.Add(CustomCommandsEnabled(settings));
            var hidden = HiddenCommandSlots(settings);
            if (hidden.Count > 0)
            {
                items.Add(CommandLaunchesHidden(hidden));
            }
        }

        if (settings.AlwaysHideTrayIcon)
        {
            items.Add(TrayIconHidden(context.StartsWithWindows));
        }

        if (context.StartsWithWindows)
        {
            items.Add(new RiskItem(Ids.RunsAtStartup, RiskSeverity.Info, "Starts when you sign in",
                "One Run entry under your user account (HKCU) starts the app at sign-in. It does not affect other accounts. " +
                "Turn off Run at startup to remove the entry."));
        }

        return BySeverity(items);
    }

    /// <summary>
    /// What the user agrees to in the consent dialog shown before the app restarts as administrator.
    /// </summary>
    /// <param name="everyStart">True when the consent is for "Always run as administrator" rather than one restart.</param>
    public static IReadOnlyList<RiskItem> DescribeElevation(ApplicationSettings settings, InstallLocationAssessment install, bool everyStart)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(install);
        var hotkeys = settings.EnableCustomCommands ? "corner actions and custom hotkey commands" : "corner actions";
        var items = new List<RiskItem>
        {
            new(Ids.ElevationControlsElevatedWindows, RiskSeverity.Caution, "Hotkeys can control elevated windows",
                $"With administrator rights, the app's {hotkeys} can send keys to windows that run as administrator. " +
                "That is the reason to elevate, and it is also the risk: whatever the corners are set to do can act on those windows."),
            new(Ids.ElevationLaunchesWithoutAdmin, RiskSeverity.Info, "Programs it opens do not get admin rights",
                "Programs, documents and links the app opens still start without administrator rights. " +
                "A program that needs them shows its own UAC prompt."),
            new(Ids.ElevationSettingsApproval, RiskSeverity.Info, "Setting changes need your approval",
                "Any program running under your account can change the settings file. Before an elevated session uses " +
                "changed corner actions or commands, the app shows you each change and asks you to approve it.")
        };

        if (!IsProtected(install))
        {
            items.Add(new RiskItem(Ids.ElevationUserWritableInstall, RiskSeverity.Warning, "Program files can be replaced",
                "The app's own files are in a folder that any program running under your account can change. If one replaced " +
                "them, approving the UAC prompt would give that program administrator rights. Install the app under " +
                "Program Files before running it as administrator."));
        }

        if (everyStart)
        {
            items.Add(new RiskItem(Ids.ElevationPromptEveryStart, RiskSeverity.Caution, "A UAC prompt at every sign-in",
                "Windows will show a UAC prompt for this app each time it starts, including when you sign in. " +
                "Approve it only when you expect it, for example right after signing in or starting the app yourself."));
        }

        return BySeverity(items);
    }

    /// <summary>Short, verifiable statements about what the app does, for the Security page.</summary>
    public static IReadOnlyList<string> Facts(SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = string.IsNullOrWhiteSpace(context.SettingsPath)
            ? "an unknown location"
            : DisplayText.ForDisplay(context.SettingsPath, maxLength: 260);

        return
        [
            "WinXCornersPlus watches only the pointer position to detect corners. It never records keystrokes.",
            "It makes no network connections. The update link only opens the releases page in your browser.",
            $"Your settings are stored in {path}.",
            context.StartsWithWindows
                ? "It has one Run entry for your user account only, because Run at startup is on."
                : "It adds a Run entry for your user account only when Run at startup is on, which it currently is not.",
            context.IsElevated
                ? "It is running as administrator. Programs and links it opens start without administrator rights."
                : "It is running without administrator rights.",
            "This build is not code-signed, so Windows shows \"Unknown publisher\" when it asks about this app."
        ];
    }

    /// <summary>
    /// The item to show next to a switch at the moment it is turned on: <c>AlwaysRunAsAdministrator</c>,
    /// <c>EnableCustomCommands</c>, <c>AlwaysHideTrayIcon</c> or <see cref="LaunchHiddenSetting"/>. Null for any
    /// other name. The result describes the switch as on, whatever <paramref name="settings"/> currently says.
    /// </summary>
    public static RiskItem? ForSetting(string? settingName, ApplicationSettings settings, SecurityContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        return settingName switch
        {
            nameof(ApplicationSettings.AlwaysRunAsAdministrator) => AlwaysRunAsAdministrator(IsProtected(context.Install)),
            nameof(ApplicationSettings.EnableCustomCommands) => CustomCommandsEnabled(settings),
            nameof(ApplicationSettings.AlwaysHideTrayIcon) => TrayIconHidden(context.StartsWithWindows),
            LaunchHiddenSetting => CommandLaunchesHidden([]),
            _ => null
        };
    }

    private static bool IsProtected(InstallLocationAssessment install) => install.Kind == InstallLocationKind.Protected;

    private static RiskItem ElevatedFromUserWritableFolder() =>
        new(Ids.ElevatedFromUserWritableFolder, RiskSeverity.Warning, "Administrator rights from a changeable folder",
            "This session runs as administrator from a folder that programs running under your account can change, " +
            "so they could replace the app's files and get administrator rights next time. Move the app under " +
            "Program Files, or run it without administrator rights.");

    private static RiskItem AlwaysRunAsAdministrator(bool installProtected)
    {
        const string Prompt = "Windows will show a UAC prompt each time the app starts, including at sign-in. Approve it only when you expect it.";
        return installProtected
            ? new RiskItem(Ids.AlwaysRunAsAdministrator, RiskSeverity.Caution, "Always runs as administrator",
                Prompt + " Programs the app opens still start without administrator rights.")
            : new RiskItem(Ids.AlwaysRunAsAdministrator, RiskSeverity.Warning, "Always runs as administrator",
                Prompt + " The app's files are in a folder other programs can change, so approving could give one of them " +
                "administrator rights. Install under Program Files first.");
    }

    private static RiskItem CustomCommandsEnabled(ApplicationSettings settings)
    {
        var count = (settings.CustomCommands ?? [])
            .Take(ApplicationSettings.CustomCommandCount)
            .Count(static command => !string.IsNullOrWhiteSpace(command?.Command));
        var configured = count switch
        {
            0 => "No custom commands are set up yet.",
            1 => "1 custom command is set up.",
            _ => string.Create(CultureInfo.InvariantCulture, $"{count} custom commands are set up.")
        };

        return new RiskItem(Ids.CustomCommandsEnabled, RiskSeverity.Caution, "Custom commands are on",
            configured + " A command runs as soon as the pointer touches a corner assigned to it. Any program running " +
            "under your account can edit the settings file and change what a command runs.");
    }

    private static RiskItem CommandLaunchesHidden(IReadOnlyList<int> slots)
    {
        var which = slots.Count switch
        {
            0 => "This command starts",
            1 => $"Command {slots[0] + 1} starts",
            _ => "Commands " + string.Join(", ", slots.Select(static slot => slot + 1)) + " start"
        };

        return new RiskItem(Ids.CommandLaunchesHidden, RiskSeverity.Caution, "Command runs with no visible window",
            which + " with no window, so you will not see it run and cannot close it from a window. " +
            "To stop it, end it in Task Manager.");
    }

    private static RiskItem TrayIconHidden(bool startsWithWindows)
    {
        var autostart = startsWithWindows
            ? " Because it also starts at sign-in, it can run without any visible sign."
            : string.Empty;
        return new RiskItem(Ids.TrayIconHidden, startsWithWindows ? RiskSeverity.Caution : RiskSeverity.Info,
            "Tray icon is hidden",
            "The app keeps running with no icon in the notification area." + autostart +
            " To get back to Settings, start WinXCornersPlus again; the running copy opens its Settings window.");
    }

    /// <summary>
    /// Slots whose hidden-window switch matters: only valid commands that launch a program. Hotkey and conditional
    /// commands open no window, so "starts with no window" would be false for them.
    /// </summary>
    private static List<int> HiddenCommandSlots(ApplicationSettings settings)
    {
        var commands = settings.CustomCommands ?? [];
        return Enumerable.Range(0, Math.Min(commands.Length, ApplicationSettings.CustomCommandCount))
            .Where(index => commands[index] is { LaunchHidden: true } command &&
                            CustomCommandAnalyzer.Analyze(command.Command) is { IsValid: true, Kind: CustomCommandKind.Launch })
            .ToList();
    }

    /// <summary>Most severe first; equal severities keep their documented order (OrderBy is stable).</summary>
    private static IReadOnlyList<RiskItem> BySeverity(List<RiskItem> items) =>
        items.OrderByDescending(static item => item.Severity).ToArray();
}
