using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>How <see cref="IAppHost.TryRestart"/> starts the replacement process.</summary>
internal enum RestartMode
{
    /// <summary>Restart with the rights the app has now.</summary>
    SameRights,

    /// <summary>Restart elevated. Windows shows a UAC prompt; the user may decline.</summary>
    AsAdministrator,

    /// <summary>Restart with normal rights and do not auto-elevate on that start.</summary>
    WithoutAdministrator
}

/// <summary>
/// Everything the windows and dialogs need from the platform layer. Forms depend on this interface
/// only, never on Win32 or the registry directly, so the security-relevant side effects (launching,
/// elevation, autostart) have exactly one implementation.
/// All members are called on the UI thread and none of them throws for an expected failure.
/// </summary>
internal interface IAppHost
{
    BoundedLog Log { get; }

    /// <summary>The single owner of the live settings. Subscribe to its Changed event to react to edits.</summary>
    SettingsSession Settings { get; }

    bool IsElevated { get; }

    /// <summary>Product version for display, e.g. "1.5.0".</summary>
    string VersionText { get; }

    string ExecutablePath { get; }

    /// <summary>A fresh snapshot of the facts the risk text is built from.</summary>
    SecurityContext GetSecurityContext();

    /// <summary>
    /// Opens an https link in the default browser. When the app is elevated the browser is started without
    /// administrator rights through the desktop shell, or not at all; only on an account with no normal-rights
    /// context (UAC off, built-in Administrator) does it get the app's rights. Failures are logged.
    /// </summary>
    void OpenLink(string httpsUrl);

    /// <summary>Shows the folder that holds settings.json in File Explorer (launched like <see cref="OpenLink"/>).</summary>
    void OpenSettingsFolder();

    /// <summary>True only when the current user's Run entry points at this executable.</summary>
    bool IsStartupEnabled();

    bool TrySetStartupEnabled(bool enabled, out string? error);

    /// <summary>
    /// Saves settings, starts a replacement process and then asks this one to exit. Returns false
    /// (and keeps running) if the replacement could not be started, e.g. the UAC prompt was declined.
    /// </summary>
    bool TryRestart(RestartMode mode, out string? error);

    /// <summary>Clean shutdown: save settings, remove the tray icon, end the message loop.</summary>
    void RequestExit();
}
