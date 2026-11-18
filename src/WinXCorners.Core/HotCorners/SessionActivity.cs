namespace WinXCorners.Core.HotCorners;

/// <summary>Session changes Windows reports (<c>WM_WTSSESSION_CHANGE</c>); the values are Windows' own codes.</summary>
public enum SessionChange
{
    ConsoleConnect = 1,
    ConsoleDisconnect = 2,
    RemoteConnect = 3,
    RemoteDisconnect = 4,
    SessionLogon = 5,
    SessionLogoff = 6,
    SessionLock = 7,
    SessionUnlock = 8,
    SessionRemoteControl = 9
}

/// <summary>
/// Whether this session is in use at the moment: not locked and connected to a console or a remote client. Hot corners
/// pause while it is not (review V2 D2). Lock and connection are tracked separately, because Windows reports them
/// separately and in either order (a locked session can be disconnected, reconnected, and only then unlocked).
/// </summary>
/// <remarks>Starts active: the app is started by a user on an unlocked desktop. Not thread-safe; use on the UI thread.</remarks>
public sealed class SessionActivity
{
    private bool _locked;
    private bool _disconnected;

    /// <summary>True when the session is neither locked nor disconnected.</summary>
    public bool IsActive => !_locked && !_disconnected;

    /// <summary>
    /// Applies one notification. Returns true when <see cref="IsActive"/> changed. Codes this class does not know
    /// (and logon, logoff, remote control) change nothing.
    /// </summary>
    public bool Apply(SessionChange change)
    {
        var before = IsActive;
        switch (change)
        {
            case SessionChange.SessionLock:
                _locked = true;
                break;
            case SessionChange.SessionUnlock:
                _locked = false;
                break;
            case SessionChange.ConsoleDisconnect:
            case SessionChange.RemoteDisconnect:
                _disconnected = true;
                break;
            case SessionChange.ConsoleConnect:
            case SessionChange.RemoteConnect:
                _disconnected = false;
                break;
        }

        return before != IsActive;
    }

    /// <summary>
    /// The pointer fact for <see cref="HotCornerInput.PointerAvailable"/>: the position could be read, this session is
    /// active, and the input desktop is the one this app runs on.
    /// </summary>
    public bool IsPointerAvailable(bool positionRead, bool inputDesktopIsOurs) => positionRead && inputDesktopIsOurs && IsActive;
}
