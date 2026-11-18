using System.ComponentModel;
using System.Runtime.InteropServices;
using WinXCorners.Core.Actions;

namespace WinXCorners.App;

/// <summary>Performs the screen saver / display off / Start menu system commands and the workstation lock.</summary>
internal sealed class Win32SystemActions : ISystemActions
{
    /// <summary>
    /// Posts WM_SYSCOMMAND to <paramref name="window"/> (the app's own main window), whose default window procedure
    /// carries the command out. Posted rather than sent so the action worker never blocks on the UI thread.
    /// </summary>
    public bool TryPostSystemCommand(IntPtr window, SystemCommandKind kind, out string? error)
    {
        if (window == IntPtr.Zero)
        {
            error = "The main window is not available yet.";
            return false;
        }

        (int command, int parameter) = kind switch
        {
            SystemCommandKind.StartScreenSaver => (NativeMethods.SC_SCREENSAVE, 0),
            SystemCommandKind.TurnDisplayOff => (NativeMethods.SC_MONITORPOWER, NativeMethods.MonitorPowerOff),
            SystemCommandKind.OpenStartMenu => (NativeMethods.SC_TASKLIST, 0),
            _ => (0, 0)
        };

        if (command == 0)
        {
            error = $"Unknown system command {kind}.";
            return false;
        }

        if (!NativeMethods.PostMessage(window, NativeMethods.WM_SYSCOMMAND, new IntPtr(command), new IntPtr(parameter)))
        {
            error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        error = null;
        return true;
    }

    public bool TryLockWorkstation(out string? error)
    {
        if (!NativeMethods.LockWorkStation())
        {
            error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return false;
        }

        error = null;
        return true;
    }
}
