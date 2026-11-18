namespace WinXCorners.Core.Platform;

/// <summary>What the tray app does when Windows (or an installer) tells it that its session is ending.</summary>
public enum SessionEndResponse
{
    /// <summary>Not a session-end message, or the session end was cancelled: carry on.</summary>
    None,

    /// <summary>The session may end soon: save pending settings now, keep running.</summary>
    SaveSettings,

    /// <summary>The session is ending, or an installer or Restart Manager needs the app closed: run the normal shutdown.</summary>
    Exit
}

/// <summary>
/// Decides how to react to <c>WM_QUERYENDSESSION</c> / <c>WM_ENDSESSION</c>.
/// </summary>
/// <remarks>
/// Besides log-off and shutdown, these are the messages the Windows Installer's Restart Manager and the WiX
/// <c>CloseApplication</c> action send (with <see cref="EndSessionCloseApp"/>) when an upgrade or uninstall needs the
/// program's files. A tray app with no visible window never receives <c>WM_CLOSE</c> from the user, so this is the
/// one clean way to ask it to exit: it then saves its settings and removes its tray icon instead of being terminated.
/// The app never relaunches itself afterwards (it does not register with the Restart Manager), so an elevated
/// installer cannot start it elevated.
/// </remarks>
public static class SessionEndPolicy
{
    /// <summary>WM_QUERYENDSESSION.</summary>
    public const int WmQueryEndSession = 0x0011;

    /// <summary>WM_ENDSESSION.</summary>
    public const int WmEndSession = 0x0016;

    /// <summary>ENDSESSION_CLOSEAPP: the lParam flag set by the Restart Manager and installers.</summary>
    public const long EndSessionCloseApp = 0x00000001;

    /// <param name="message">The window message.</param>
    /// <param name="wParam">For WM_ENDSESSION: non-zero when the session really ends, zero when it was cancelled.</param>
    public static SessionEndResponse Decide(int message, long wParam)
    {
        return message switch
        {
            WmQueryEndSession => SessionEndResponse.SaveSettings,
            WmEndSession when wParam != 0 => SessionEndResponse.Exit,
            _ => SessionEndResponse.None
        };
    }
}
