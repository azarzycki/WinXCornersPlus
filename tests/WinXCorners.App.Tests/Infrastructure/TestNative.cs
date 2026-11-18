using System.Runtime.InteropServices;

namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>Win32 calls the tests need to set up windows; the product's own imports live in NativeMethods.</summary>
internal static class TestNative
{
    internal const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_CAPTION = 0x00C00000;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const int SW_SHOW = 5;
    internal const int SW_SHOWMAXIMIZED = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        internal uint cbSize;
        internal uint style;
        internal IntPtr lpfnWndProc;
        internal int cbClsExtra;
        internal int cbWndExtra;
        internal IntPtr hInstance;
        internal IntPtr hIcon;
        internal IntPtr hCursor;
        internal IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string lpszClassName;
        internal IntPtr hIconSm;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    internal static extern IntPtr GetProcAddress(IntPtr module, string procName);

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern ushort RegisterClassEx(ref WNDCLASSEXW windowClass);

    [DllImport("user32.dll", EntryPoint = "UnregisterClassW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClass(string className, IntPtr instance);

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr window, [Out] char[] text, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMitigationPolicy(IntPtr process, int policy, out uint flags, UIntPtr length);

    /// <summary>Every top-level window (visible or not) of a process: (title, class, visible).</summary>
    internal static List<(string Title, string Class, bool Visible)> TopLevelWindowsOf(int processId)
    {
        var windows = new List<(string, string, bool)>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner == (uint)processId)
            {
                var title = new char[512];
                var className = new char[256];
                var titleLength = GetWindowText(window, title, title.Length);
                var classLength = GetClassName(window, className, className.Length);
                windows.Add((new string(title, 0, titleLength), new string(className, 0, classLength), IsWindowVisible(window)));
            }

            return true;
        }, IntPtr.Zero);
        return windows;
    }
}
