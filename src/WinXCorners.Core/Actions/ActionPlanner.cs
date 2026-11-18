using WinXCorners.Core.Input;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Actions;

/// <summary>A Windows command the app performs by posting <c>WM_SYSCOMMAND</c> to its own window.</summary>
public enum SystemCommandKind
{
    /// <summary><c>SC_SCREENSAVE</c>.</summary>
    StartScreenSaver,

    /// <summary><c>SC_MONITORPOWER</c> with lParam 2 (power off).</summary>
    TurnDisplayOff,

    /// <summary><c>SC_TASKLIST</c>, which opens the Start menu.</summary>
    OpenStartMenu
}

/// <summary>What performing a corner action means, decided without touching Windows.</summary>
public abstract record ActionPlan
{
    private ActionPlan()
    {
    }

    /// <summary>Do nothing; <paramref name="Reason"/> is for the log.</summary>
    public sealed record Nothing(string Reason) : ActionPlan;

    /// <summary>Inject the given validated hotkey sequence.</summary>
    public sealed record SendKeys(HotkeySequence Sequence) : ActionPlan;

    /// <summary>Evaluate the conditions against the current windows, then inject the selected sequence (if any).</summary>
    public sealed record ConditionalKeys(ConditionalHotkeyExpression Expression) : ActionPlan;

    /// <summary>Hand the request to the launcher, which plans it with <see cref="LaunchPlanner"/>.</summary>
    public sealed record Launch(LaunchRequest Request) : ActionPlan;

    /// <summary>Post the given <c>WM_SYSCOMMAND</c>.</summary>
    public sealed record SystemCommand(SystemCommandKind Kind) : ActionPlan;

    /// <summary>Call <c>LockWorkStation</c>.</summary>
    public sealed record LockWorkstation : ActionPlan;
}

/// <summary>
/// Maps a corner action id to the side effect the app must perform. Same behaviour as the original
/// <c>HotCornerActions.Execute</c>, except that built-in programs are absolute system paths (review S3)
/// and custom commands are validated in full before anything runs (review B5/B9).
/// </summary>
public static class ActionPlanner
{
    private static readonly HotkeySequence Escape = ParseBuiltIn("escape");
    private static readonly HotkeySequence WinTab = ParseBuiltIn("_win+tab");
    private static readonly HotkeySequence WinD = ParseBuiltIn("_win+d");
    private static readonly HotkeySequence WinA = ParseBuiltIn("_win+a");
    private static readonly HotkeySequence WinN = ParseBuiltIn("_win+n");
    private static readonly HotkeySequence WinHome = ParseBuiltIn("_win+home");

    /// <summary>
    /// Plans <paramref name="actionId"/>. Never throws for any content of <paramref name="settings"/>, including
    /// unsanitized values (null command arrays, elements or strings).
    /// </summary>
    /// <param name="actionId">An id from <see cref="ActionIds"/>; null, unknown and "none" plan <see cref="ActionPlan.Nothing"/>.</param>
    /// <param name="settings">Settings snapshot the custom command slots are read from.</param>
    /// <param name="directories">System folders used for the absolute paths of built-in programs.</param>
    /// <param name="taskViewIsForeground">
    /// True when Task View is already in front: "all-windows" then closes it with Escape instead of toggling it
    /// with Win+Tab (which would leave it open on some builds).
    /// </param>
    public static ActionPlan Plan(string? actionId, ApplicationSettings settings, SystemDirectories directories, bool taskViewIsForeground)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(directories);

        switch (actionId)
        {
            case ActionIds.FileExplorer:
                return new ActionPlan.Launch(SystemTargets.FileExplorer(directories));
            case ActionIds.Settings:
                return new ActionPlan.Launch(SystemTargets.SystemSettings());
            case ActionIds.TaskManager:
                return new ActionPlan.Launch(SystemTargets.TaskManager(directories));
            case ActionIds.AllWindows:
                return new ActionPlan.SendKeys(taskViewIsForeground ? Escape : WinTab);
            case ActionIds.Desktop:
                return new ActionPlan.SendKeys(WinD);
            case ActionIds.ActionCenter:
                return new ActionPlan.SendKeys(WinA);
            case ActionIds.NotificationCenter:
                return new ActionPlan.SendKeys(WinN);
            case ActionIds.HideOtherWindows:
                return new ActionPlan.SendKeys(WinHome);
            case ActionIds.ScreenSaver:
                return new ActionPlan.SystemCommand(SystemCommandKind.StartScreenSaver);
            case ActionIds.MonitorsOff:
                return new ActionPlan.SystemCommand(SystemCommandKind.TurnDisplayOff);
            case ActionIds.StartMenu:
                return new ActionPlan.SystemCommand(SystemCommandKind.OpenStartMenu);
            case ActionIds.LockScreen:
                return new ActionPlan.LockWorkstation();
            default:
                return ActionCatalog.TryGetCustomIndex(actionId, out var index)
                    ? PlanCustom(settings, index)
                    : new ActionPlan.Nothing("No action is assigned.");
        }
    }

    /// <summary>Parses one of the fixed built-in sequences; a failure is a programming error.</summary>
    internal static HotkeySequence ParseBuiltIn(string text)
    {
        return HotkeySequence.TryParse(text, out var sequence, out var error)
            ? sequence
            : throw new InvalidOperationException($"Built-in hotkey '{text}' is invalid: {error}");
    }

    private static ActionPlan PlanCustom(ApplicationSettings settings, int index)
    {
        var slot = index + 1;
        if (!settings.EnableCustomCommands)
        {
            return new ActionPlan.Nothing($"Custom command {slot} skipped: custom commands are disabled.");
        }

        var commands = settings.CustomCommands;
        var command = commands is not null && index < commands.Length ? commands[index] : null;
        var analysis = CustomCommandAnalyzer.Analyze(command?.Command);
        if (!analysis.IsValid)
        {
            return new ActionPlan.Nothing($"Custom command {slot} is invalid: {analysis.Error}");
        }

        return analysis.Kind switch
        {
            CustomCommandKind.HotkeySequence => new ActionPlan.SendKeys(analysis.Sequence!),
            CustomCommandKind.ConditionalHotkey => new ActionPlan.ConditionalKeys(analysis.Conditional!),
            CustomCommandKind.Launch => new ActionPlan.Launch(new LaunchRequest(
                analysis.LaunchTarget!,
                command!.Parameters ?? string.Empty,
                command.LaunchHidden,
                LaunchTargetKind.CustomCommand)),
            _ => new ActionPlan.Nothing($"Custom command {slot} skipped: it is empty.")
        };
    }
}
