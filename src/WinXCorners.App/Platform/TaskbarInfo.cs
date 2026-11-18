using System.Runtime.InteropServices;
using WinXCorners.Core.Layout;

namespace WinXCorners.App;

/// <summary>Where the primary taskbar is, as reported by the shell (<c>ABM_GETTASKBARPOS</c>).</summary>
internal readonly record struct TaskbarInfo(Rectangle Bounds, TaskbarEdge Edge)
{
    /// <summary>The primary taskbar, or null when the shell does not answer (e.g. explorer is not running).</summary>
    internal static TaskbarInfo? TryGetPrimaryTaskbar()
    {
        var data = new NativeMethods.APPBARDATA { cbSize = (uint)Marshal.SizeOf<NativeMethods.APPBARDATA>() };
        if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_GETTASKBARPOS, ref data) == UIntPtr.Zero)
        {
            return null;
        }

        var bounds = Rectangle.FromLTRB(data.rc.Left, data.rc.Top, data.rc.Right, data.rc.Bottom);

        // TaskbarEdge uses the ABE_* values (0-3); anything else is treated as the default bottom edge.
        var edge = data.uEdge <= (uint)TaskbarEdge.Bottom ? (TaskbarEdge)(int)data.uEdge : TaskbarEdge.Bottom;
        return new TaskbarInfo(bounds, edge);
    }
}
