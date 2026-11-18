using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using WinXCorners.Core.HotCorners;

namespace WinXCorners.App;

/// <summary>
/// Gathers the facts about the foreground window that Core's <see cref="FullScreenHeuristics"/> decides on.
/// Safe to call from any thread.
/// </summary>
internal sealed class ForegroundWindowInspector
{
    private const string DesktopViewClass = "SHELLDLL_DefView";
    private const string ExplorerProcessName = "explorer";

    internal ForegroundWindowSnapshot Capture()
    {
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(handle))
        {
            return new ForegroundWindowSnapshot(
                Exists: false, IsShellWindow: false, IsVisible: false, IsMinimized: false, ClassName: string.Empty,
                BelongsToExplorer: false, HostsDesktopView: false, HasCaption: false, IsClickThrough: false,
                WindowBounds: Rectangle.Empty, MonitorBounds: Rectangle.Empty);
        }

        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_STYLE).ToInt64();
        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        var windowBounds = NativeMethods.GetWindowRect(handle, out var rect) ? ToRectangle(rect) : Rectangle.Empty;

        return new ForegroundWindowSnapshot(
            Exists: true,
            IsShellWindow: handle == NativeMethods.GetShellWindow(),
            IsVisible: NativeMethods.IsWindowVisible(handle),
            IsMinimized: NativeMethods.IsIconic(handle),
            ClassName: Win32Text.GetClassName(handle),
            BelongsToExplorer: BelongsToExplorer(handle),
            HostsDesktopView: NativeMethods.FindWindowEx(handle, IntPtr.Zero, DesktopViewClass, null) != IntPtr.Zero,
            HasCaption: (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION,
            IsClickThrough: (exStyle & NativeMethods.WS_EX_TRANSPARENT) != 0,
            WindowBounds: windowBounds,
            MonitorBounds: GetMonitorBounds(handle));
    }

    internal bool IsFullScreenForeground()
    {
        var snapshot = Capture();
        return FullScreenHeuristics.IsFullScreen(in snapshot);
    }

    internal bool IsTaskViewForeground()
    {
        var handle = NativeMethods.GetForegroundWindow();
        return handle != IntPtr.Zero && FullScreenHeuristics.IsTaskViewClass(Win32Text.GetClassName(handle));
    }

    /// <summary>
    /// A process that has exited or that we may not inspect (e.g. a protected or elevated process when we are not)
    /// is reported as "not explorer"; the other checks still apply.
    /// </summary>
    private static bool BelongsToExplorer(IntPtr handle)
    {
        _ = NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0 || processId > int.MaxValue)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, ExplorerProcessName, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false; // The process is no longer running.
        }
        catch (InvalidOperationException)
        {
            return false; // It exited between the lookup and reading its name.
        }
        catch (Win32Exception)
        {
            return false; // Access denied.
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static Rectangle GetMonitorBounds(IntPtr handle)
    {
        var monitor = NativeMethods.MonitorFromWindow(handle, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return Rectangle.Empty;
        }

        var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        return NativeMethods.GetMonitorInfo(monitor, ref info) ? ToRectangle(info.rcMonitor) : Rectangle.Empty;
    }

    private static Rectangle ToRectangle(NativeMethods.RECT rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
}
