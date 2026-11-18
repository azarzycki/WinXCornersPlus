namespace WinXCorners.Core.Security;

/// <summary>What the user (or a startup step) asked a restart to do.</summary>
public enum RestartRequest
{
    /// <summary>Restart with the rights the app has now (the tray menu's "Restart").</summary>
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

    /// <summary>Start it through the UAC prompt (the <c>runas</c> verb), which shows the user which program asks.</summary>
    ThroughElevationPrompt,

    /// <summary>Ask the desktop shell to start it, so it gets the shell's normal-rights token (fails closed).</summary>
    ThroughDesktopShell,

    /// <summary>The request cannot be honoured in this situation; the app keeps running (or exits, at startup).</summary>
    NotPossible
}

/// <summary>
/// Decides how a restart is performed (review V2 D5). An elevated session must never silently re-run a binary that
/// programs without administrator rights could have replaced: that would hand them the elevated token with no prompt.
/// So an elevated restart starts the executable directly only when its folder is <see cref="InstallLocationKind.Protected"/>;
/// otherwise it goes through the UAC prompt, where the user sees (and can refuse) what asks for administrator rights.
/// </summary>
public static class RestartPolicy
{
    /// <summary>The user-facing reason for <see cref="RestartMethod.NotPossible"/>.</summary>
    public const string NoStandardRightsText =
        "On this account everything runs with administrator rights (User Account Control is off, or this is the " +
        "built-in Administrator account), so WinXCornersPlus cannot be started without them.";

    public static RestartMethod Decide(RestartRequest request, ElevationMode mode, InstallLocationKind install)
    {
        if (mode == ElevationMode.NotElevated)
        {
            return request == RestartRequest.AsAdministrator ? RestartMethod.ThroughElevationPrompt : RestartMethod.StartDirectly;
        }

        if (request == RestartRequest.WithoutAdministrator)
        {
            return mode == ElevationMode.ElevatedWithStandardShell ? RestartMethod.ThroughDesktopShell : RestartMethod.NotPossible;
        }

        // SameRights or AsAdministrator from an elevated session: both mean "elevated again".
        return install == InstallLocationKind.Protected ? RestartMethod.StartDirectly : RestartMethod.ThroughElevationPrompt;
    }
}
