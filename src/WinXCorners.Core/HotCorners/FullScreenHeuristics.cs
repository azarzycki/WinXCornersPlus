using System.Drawing;

namespace WinXCorners.Core.HotCorners;

/// <summary>Facts about the foreground window, gathered by the app with Win32 calls.</summary>
/// <param name="Exists">There is a foreground window and it is a valid window handle.</param>
/// <param name="IsShellWindow">It is the desktop shell window (GetShellWindow).</param>
/// <param name="ClassName">Its window class name.</param>
/// <param name="BelongsToExplorer">Its process is explorer.exe.</param>
/// <param name="HostsDesktopView">It has a SHELLDLL_DefView child (the desktop icons).</param>
/// <param name="HasCaption">Its style includes WS_CAPTION (a normal titled window, even if maximised).</param>
/// <param name="IsClickThrough">Its extended style includes WS_EX_TRANSPARENT (an overlay).</param>
/// <param name="WindowBounds">GetWindowRect, in screen coordinates.</param>
/// <param name="MonitorBounds">Bounds of the monitor the window is on.</param>
public readonly record struct ForegroundWindowSnapshot(
    bool Exists,
    bool IsShellWindow,
    bool IsVisible,
    bool IsMinimized,
    string ClassName,
    bool BelongsToExplorer,
    bool HostsDesktopView,
    bool HasCaption,
    bool IsClickThrough,
    Rectangle WindowBounds,
    Rectangle MonitorBounds);

/// <summary>
/// Decides whether the foreground window is a full-screen application (a game, a video player, a
/// presentation), in which case hot corners are ignored when the user asked for that.
/// </summary>
/// <remarks>
/// The original <c>FullScreenDetector.ShouldBlockAction</c> exempted "all-windows" when Task View was in
/// the foreground. That branch was unreachable: the Task View classes are also shell classes, so
/// such a window is never reported as full screen in the first place. The blocking answer is therefore
/// simply <see cref="IsFullScreen"/>, for every action. The original's maximised/not-maximised split
/// returned the same value on both sides and is gone too (review B8).
/// </remarks>
public static class FullScreenHeuristics
{
    private static readonly string[] TaskViewClasses = ["XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow"];

    private static readonly string[] ShellClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", .. TaskViewClasses];

    /// <summary>True for the window classes of Task View / the Windows 11 shell overlays. Case-insensitive.</summary>
    public static bool IsTaskViewClass(string? className) => Matches(TaskViewClasses, className);

    /// <summary>True for desktop, taskbar and shell-overlay classes, which are never "full-screen apps". Case-insensitive.</summary>
    public static bool IsShellClass(string? className) => Matches(ShellClasses, className);

    /// <summary>
    /// True when the snapshot describes a visible, caption-less, non-shell window that covers its
    /// whole monitor.
    /// </summary>
    public static bool IsFullScreen(in ForegroundWindowSnapshot snapshot)
    {
        if (!snapshot.Exists || snapshot.IsShellWindow || !snapshot.IsVisible || snapshot.IsMinimized ||
            IsShellClass(snapshot.ClassName) || snapshot.BelongsToExplorer || snapshot.HostsDesktopView ||
            snapshot.HasCaption || snapshot.IsClickThrough)
        {
            return false;
        }

        var monitor = snapshot.MonitorBounds;
        if (monitor.Width <= 0 || monitor.Height <= 0)
        {
            return false;
        }

        // The original only compared sizes; requiring the window to actually cover the monitor stops a
        // monitor-sized window that is partly off-screen from counting. Larger windows still count
        // (some games extend a few pixels past the monitor).
        return snapshot.WindowBounds.Contains(monitor);
    }

    private static bool Matches(string[] classes, string? className)
    {
        if (string.IsNullOrEmpty(className))
        {
            return false;
        }

        foreach (var candidate in classes)
        {
            if (string.Equals(candidate, className, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
