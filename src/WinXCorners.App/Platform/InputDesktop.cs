namespace WinXCorners.App;

/// <summary>
/// The pointer and desktop facts hot corners need (review V2 D2). WinForms' Cursor.Position ignores GetCursorPos
/// failing and reports (0,0), the top-left pixel, while the lock screen, a UAC prompt or Ctrl+Alt+Del has the input.
/// </summary>
internal sealed class InputDesktop
{
    private readonly string? _ownDesktopName;

    /// <summary>Call on the thread that owns the app's windows: its desktop is the one compared against.</summary>
    internal InputDesktop()
    {
        _ownDesktopName = NameOf(NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId()));
    }

    /// <summary>The pointer position, or null when Windows does not give it to this process.</summary>
    internal static Point? TryGetCursorPosition() =>
        NativeMethods.GetCursorPos(out var point) ? new Point(point.X, point.Y) : null;

    /// <summary>
    /// True when the desktop receiving input is this app's desktop. The secure desktop (Winlogon) cannot be opened by
    /// the user's processes, elevated or not, so failing to open the input desktop also means "not ours".
    /// </summary>
    internal bool IsInputDesktopOurs()
    {
        var input = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
        if (input == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var name = NameOf(input);
            return name is not null && string.Equals(name, _ownDesktopName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            NativeMethods.CloseDesktop(input);
        }
    }

    private static string? NameOf(IntPtr desktop)
    {
        if (desktop == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new char[64];
        if (!NativeMethods.GetUserObjectInformation(desktop, NativeMethods.UOI_NAME, buffer, (uint)(buffer.Length * sizeof(char)), out var needed))
        {
            if (needed == 0 || needed > 4096)
            {
                return null;
            }

            buffer = new char[(needed / sizeof(char)) + 1];
            if (!NativeMethods.GetUserObjectInformation(desktop, NativeMethods.UOI_NAME, buffer, (uint)(buffer.Length * sizeof(char)), out _))
            {
                return null;
            }
        }

        var end = Array.IndexOf(buffer, '\0');
        return new string(buffer, 0, end < 0 ? buffer.Length : end);
    }
}
