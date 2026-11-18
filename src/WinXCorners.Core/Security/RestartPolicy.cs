namespace WinXCorners.Core.Security;

/// <summary>What the user (or a startup step) asked a restart to do.</summary>
public enum RestartRequest
{
    /// <summary>Restart with the rights the app has now, where that is safe (the tray menu's "Restart").</summary>
    SameRights,

    /// <summary>Restart elevated (the tray menu's "Restart as administrator", or "Always run as administrator").</summary>
    AsAdministrator,

    /// <summary>Restart with normal rights (the elevated session was not approved).</summary>
    WithoutAdministrator
}

/// <summary>How the replacement process is started.</summary>
public enum RestartMethod
{
    /// <summary>Start the executable directly with this process's own token.</summary>
    StartDirectly,

    /// <summary>
    /// Start it through the UAC prompt (the <c>runas</c> verb). Only ever chosen for a process that is NOT elevated:
    /// an elevated process gets no prompt from <c>runas</c>, so the new process would be elevated silently.
    /// </summary>
    ThroughElevationPrompt,

    /// <summary>Ask the desktop shell to start it, so it gets the shell's normal-rights token (fails closed).</summary>
    ThroughDesktopShell,

    /// <summary>The request cannot be honoured in this situation; the app keeps running (or exits, at startup).</summary>
    NotPossible
}

/// <summary>The outcome of <see cref="RestartPolicy.Decide"/>.</summary>
/// <param name="Method">How the replacement process is started.</param>
/// <param name="NoElevate">
/// Whether the replacement gets <c>--no-elevate</c>. False for a restart that drops administrator rights only because
/// the program files are not protected: that copy then follows the ordinary start rules, so "Always run as
/// administrator" raises a real UAC prompt from the normal-rights copy.
/// </param>
/// <param name="DropsAdministratorRights">
/// True when an elevated session restarts without administrator rights although the user did not ask for that
/// (the tray menu's "Restart" from an unprotected folder). The menu and messages say so.
/// </param>
/// <param name="RefusalText">For <see cref="RestartMethod.NotPossible"/>: one plain sentence saying why; otherwise null.</param>
public sealed record RestartDecision(RestartMethod Method, bool NoElevate, bool DropsAdministratorRights, string? RefusalText);

/// <summary>
/// Decides how a restart is performed (review V2 D5). An elevated session must never re-run, with its own token, a
/// binary that programs without administrator rights could have replaced: that would hand them administrator rights
/// with no prompt. Going through the UAC prompt (<c>runas</c>) does not help, because an elevated process gets no
/// prompt. So:
/// <list type="bullet">
/// <item>From a <see cref="InstallLocationKind.Protected"/> folder an elevated session restarts directly (only
/// administrators can change those files).</item>
/// <item>From any other folder, with a normal-rights desktop available, "Restart" starts the app WITHOUT administrator
/// rights through the desktop shell. That copy follows the ordinary start rules: with "Always run as administrator" on,
/// it asks Windows to elevate, and Windows shows a real prompt because the requester is not elevated. If the shell
/// cannot start it, nothing is started. An elevated "as administrator" restart is refused there.</item>
/// <item>On an account with no normal-rights context (UAC off, built-in Administrator) everything runs elevated anyway,
/// so the restart is direct; a restart "without administrator rights" is not possible.</item>
/// </list>
/// The <c>runas</c> path is chosen only for a process that is not elevated.
/// </summary>
public static class RestartPolicy
{
    /// <summary>The user-facing reason why a restart without administrator rights is not possible.</summary>
    public const string NoStandardRightsText =
        "On this account everything runs with administrator rights (User Account Control is off, or this is the " +
        "built-in Administrator account), so WinXCornersPlus cannot be started without them.";

    /// <summary>Why "Restart" from an unprotected folder drops administrator rights.</summary>
    public const string UnprotectedFolderText =
        "Programs without administrator rights can change the program files in this folder, so WinXCornersPlus does " +
        "not start them again with administrator rights without a Windows prompt.";

    /// <summary>The user-facing reason why an elevated session cannot restart itself as administrator here.</summary>
    public const string NoSilentElevatedRestartText =
        "WinXCornersPlus already runs as administrator, and programs without administrator rights can change the " +
        "program files in this folder, so it does not start them again with administrator rights.";

    /// <summary>The tray menu item for a plain restart.</summary>
    public const string RestartMenuText = "Restart";

    /// <summary>The tray menu item when "Restart" drops administrator rights (see <see cref="RestartDecision.DropsAdministratorRights"/>).</summary>
    public const string RestartWithoutAdministratorMenuText = "Restart without administrator rights";

    /// <summary>The whole decision table.</summary>
    /// <param name="request">What the restart is asked to do.</param>
    /// <param name="mode">This process's elevation. An undefined value is treated as elevated with a normal-rights shell.</param>
    /// <param name="install">
    /// Where the program files are. Only <see cref="InstallLocationKind.Protected"/> allows a direct elevated restart;
    /// any other value (including an undefined one) does not.
    /// </param>
    public static RestartDecision Decide(RestartRequest request, ElevationMode mode, InstallLocationKind install)
    {
        if (mode == ElevationMode.NotElevated)
        {
            return request switch
            {
                // From normal rights runas raises a real prompt: the user sees (and can refuse) what asks for rights.
                RestartRequest.AsAdministrator => Start(RestartMethod.ThroughElevationPrompt, noElevate: false),
                RestartRequest.WithoutAdministrator => Start(RestartMethod.StartDirectly, noElevate: true),
                _ => Start(RestartMethod.StartDirectly, noElevate: false)
            };
        }

        if (mode == ElevationMode.ElevatedWithoutStandardRights)
        {
            // No normal-rights context exists, so there is no boundary to protect and no way to drop the rights.
            return request == RestartRequest.WithoutAdministrator
                ? Refuse(NoStandardRightsText)
                : Start(RestartMethod.StartDirectly, noElevate: false);
        }

        // Elevated with a normal-rights desktop (or an undefined mode, treated the same way: fail safe).
        if (request == RestartRequest.WithoutAdministrator)
        {
            return Start(RestartMethod.ThroughDesktopShell, noElevate: true);
        }

        if (install == InstallLocationKind.Protected)
        {
            return Start(RestartMethod.StartDirectly, noElevate: false);
        }

        return request == RestartRequest.AsAdministrator
            ? Refuse(NoSilentElevatedRestartText)
            : new RestartDecision(RestartMethod.ThroughDesktopShell, NoElevate: false, DropsAdministratorRights: true, RefusalText: null);
    }

    /// <summary>The tray menu text for "Restart" given what it will do.</summary>
    public static string MenuText(RestartDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.DropsAdministratorRights ? RestartWithoutAdministratorMenuText : RestartMenuText;
    }

    /// <summary>The log line written when the restart starts the replacement process.</summary>
    public static string StartedText(RestartDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return decision.DropsAdministratorRights
            ? "Restarting without administrator rights. " + UnprotectedFolderText +
              " If \"Always run as administrator\" is on, Windows asks for permission when the app starts again."
            : $"Restarting ({decision.Method}).";
    }

    /// <summary>The message shown when the restart did not happen; the app keeps running.</summary>
    /// <param name="decision">What was attempted.</param>
    /// <param name="error">Why the start failed (from the platform layer), or null when unknown.</param>
    public static string FailureText(RestartDecision decision, string? error)
    {
        ArgumentNullException.ThrowIfNull(decision);
        var reason = Sentence(decision.RefusalText ?? error);
        if (decision.DropsAdministratorRights)
        {
            return "WinXCornersPlus could not restart without administrator rights: " + reason + " " +
                   UnprotectedFolderText + " It keeps running.";
        }

        return "WinXCornersPlus could not restart: " + reason + " It keeps running.";
    }

    private static RestartDecision Start(RestartMethod method, bool noElevate) =>
        new(method, noElevate, DropsAdministratorRights: false, RefusalText: null);

    private static RestartDecision Refuse(string text) =>
        new(RestartMethod.NotPossible, NoElevate: false, DropsAdministratorRights: false, RefusalText: text);

    /// <summary>Error text from Windows may or may not end with a full stop; make it exactly one sentence.</summary>
    private static string Sentence(string? text)
    {
        var trimmed = text?.Trim().TrimEnd('.').TrimEnd() ?? string.Empty;
        return (trimmed.Length == 0 ? "Unknown reason" : trimmed) + ".";
    }
}
