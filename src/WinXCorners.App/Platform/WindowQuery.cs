using WinXCorners.Core.Input;

namespace WinXCorners.App;

/// <summary>Answers Core's window conditions (<c>#[class,title]</c> / <c>@[class,title]</c>) with Win32 lookups.</summary>
internal sealed class WindowQuery : IWindowQuery
{
    public WindowInfo? GetForegroundWindow()
    {
        var handle = NativeMethods.GetForegroundWindow();
        return handle == IntPtr.Zero ? null : new WindowInfo(Win32Text.GetClassName(handle), Win32Text.GetWindowTitle(handle));
    }

    /// <summary>FindWindow: null is a wildcard (an empty string would match only an empty class or title).</summary>
    public bool WindowExists(string? className, string? title)
    {
        return NativeMethods.FindWindow(className, title) != IntPtr.Zero;
    }
}

/// <summary>Reads a window's class name and title into managed strings.</summary>
internal static class Win32Text
{
    /// <summary>Window class names are limited to 256 characters (WNDCLASS docs).</summary>
    private const int MaxClassNameLength = 256;

    /// <summary>Titles longer than this are cut; conditions compare against far shorter text.</summary>
    private const int MaxTitleLength = 4096;

    internal static string GetClassName(IntPtr handle)
    {
        var buffer = new char[MaxClassNameLength + 1];
        var length = NativeMethods.GetClassName(handle, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, Math.Min(length, buffer.Length)) : string.Empty;
    }

    internal static string GetWindowTitle(IntPtr handle)
    {
        var length = NativeMethods.GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[Math.Min(length, MaxTitleLength) + 1];
        var copied = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, Math.Min(copied, buffer.Length)) : string.Empty;
    }
}
