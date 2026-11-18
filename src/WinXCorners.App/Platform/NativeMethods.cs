using System.Runtime.InteropServices;

namespace WinXCorners.App;

/// <summary>
/// Every Win32 import the app uses, in one place, so the signatures can be reviewed together.
/// </summary>
/// <remarks>
/// All DLLs here are resolved from System32 only (see AssemblyInfo.cs, review S3).
/// <c>SetLastError = true</c> is set only where the caller actually reads the error.
/// Exported "W" entry points are named explicitly so no ANSI/Unicode probing happens.
/// </remarks>
internal static class NativeMethods
{
    // ---- Messages and commands ----------------------------------------------------------------
    internal const uint WM_NULL = 0x0000;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const int SC_SCREENSAVE = 0xF140;
    internal const int SC_MONITORPOWER = 0xF170;
    internal const int SC_TASKLIST = 0xF130;

    /// <summary>lParam for SC_MONITORPOWER: 2 = the display is being shut off.</summary>
    internal const int MonitorPowerOff = 2;

    // ---- Window styles -----------------------------------------------------------------------
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_CAPTION = 0x00C00000L;
    internal const long WS_EX_TRANSPARENT = 0x00000020L;

    // ---- Keyboard input ----------------------------------------------------------------------
    internal const uint INPUT_KEYBOARD = 1;
    internal const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint MAPVK_VK_TO_VSC = 0;

    // ---- Menus -------------------------------------------------------------------------------
    internal const uint MF_STRING = 0x00000000;
    internal const uint MF_GRAYED = 0x00000001;
    internal const uint MF_DISABLED = 0x00000002;
    internal const uint MF_SEPARATOR = 0x00000800;
    internal const uint TPM_LEFTALIGN = 0x0000;
    internal const uint TPM_TOPALIGN = 0x0000;
    internal const uint TPM_RIGHTBUTTON = 0x0002;
    internal const uint TPM_RETURNCMD = 0x0100;

    // ---- Shell app bar -----------------------------------------------------------------------
    internal const uint ABM_GETTASKBARPOS = 0x00000005;

    // ---- Monitors ----------------------------------------------------------------------------
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    // ---- Key state ---------------------------------------------------------------------------
    /// <summary>High bit of GetAsyncKeyState's result: the key is down now.</summary>
    internal const int KeyDownBit = 0x8000;

    // ---- Raw input, window position, foreground (main form) ------------------------------------
    /// <summary>HID usage page / usage of a generic mouse.</summary>
    internal const ushort HID_USAGE_PAGE_GENERIC = 0x01;
    internal const ushort HID_USAGE_GENERIC_MOUSE = 0x02;

    /// <summary>Deliver WM_INPUT even when the target window is not in the foreground.</summary>
    internal const uint RIDEV_INPUTSINK = 0x00000100;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);
    internal static readonly IntPtr HWND_NOTOPMOST = new(-2);
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOOWNERZORDER = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RAWINPUTDEVICE
    {
        internal ushort usUsagePage;
        internal ushort usUsage;
        internal uint dwFlags;
        internal IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct APPBARDATA
    {
        internal uint cbSize;
        internal IntPtr hWnd;
        internal uint uCallbackMessage;
        internal uint uEdge;
        internal RECT rc;
        internal IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        internal uint cbSize;
        internal RECT rcMonitor;
        internal RECT rcWork;
        internal uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        internal int dx;
        internal int dy;
        internal uint mouseData;
        internal uint dwFlags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        internal ushort wVk;
        internal ushort wScan;
        internal uint dwFlags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HARDWAREINPUT
    {
        internal uint uMsg;
        internal ushort wParamL;
        internal ushort wParamH;
    }

    /// <summary>
    /// The anonymous union inside INPUT. MOUSEINPUT is its largest member, so including it sizes the union
    /// correctly (32 bytes on x64, 24 on x86); SendInput rejects a cbSize that does not match.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        internal MOUSEINPUT mi;

        [FieldOffset(0)]
        internal KEYBDINPUT ki;

        [FieldOffset(0)]
        internal HARDWAREINPUT hi;
    }

    /// <summary>INPUT: 40 bytes on x64 (4-byte type, 4 bytes padding, 32-byte union), 28 on x86.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        internal uint type;
        internal InputUnion u;
    }

    // ---- user32: windows -----------------------------------------------------------------------

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, [Out] char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hWnd, [Out] char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // GetWindowLongPtrW is not exported by 32-bit user32 (it is a macro there), so importing it alone
    // breaks x86 (review B8). The helper below picks the right export for the process bitness.
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    /// <summary>GetWindowLongPtr that works in both 32-bit and 64-bit processes.</summary>
    internal static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    /// <summary>Windows 10 1607 and later; callers handle EntryPointNotFoundException on older builds.</summary>
    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    // ---- user32: keyboard ----------------------------------------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint cInputs, [In] INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    internal static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    // ---- user32: menus -------------------------------------------------------------------------

    [DllImport("user32.dll")]
    internal static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(IntPtr hMenu);

    /// <summary>With TPM_RETURNCMD the return value is the chosen command id, or 0 for none.</summary>
    [DllImport("user32.dll")]
    internal static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    // ---- user32: pointer, desktops, session notifications (review V2 D2) ------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    internal const uint DESKTOP_READOBJECTS = 0x0001;
    internal const int UOI_NAME = 2;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr OpenInputDesktop(uint dwFlags, [MarshalAs(UnmanagedType.Bool)] bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseDesktop(IntPtr hDesktop);

    /// <summary>The returned handle belongs to the thread; it must not be closed.</summary>
    [DllImport("user32.dll")]
    internal static extern IntPtr GetThreadDesktop(uint dwThreadId);

    [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] char[]? pvInfo, uint nLength, out uint lpnLengthNeeded);

    internal const int WM_WTSSESSION_CHANGE = 0x02B1;
    internal const uint NOTIFY_FOR_THIS_SESSION = 0;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint dwFlags);

    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    // ---- kernel32: process, token, mitigation ---------------------------------------------------

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenElevationTypeClass = 18;
    internal const int TokenElevationClass = 20;

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(Microsoft.Win32.SafeHandles.SafeProcessHandle processHandle, uint desiredAccess, out Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle);

    /// <summary>For TokenElevation and TokenElevationType: both return one 32-bit value.</summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(Microsoft.Win32.SafeHandles.SafeAccessTokenHandle tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    /// <summary>PROCESS_MITIGATION_POLICY.ProcessRedirectionTrustPolicy (Windows 10 21H2 / Server 2022 and later).</summary>
    internal const int ProcessRedirectionTrustPolicy = 16;

    /// <summary>PROCESS_MITIGATION_REDIRECTION_TRUST_POLICY.EnforceRedirectionTrust.</summary>
    internal const uint RedirectionTrustEnforce = 0x1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessMitigationPolicy(int mitigationPolicy, ref uint buffer, UIntPtr length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessMitigationPolicy(IntPtr hProcess, int mitigationPolicy, out uint buffer, UIntPtr length);

    // ---- kernel32 / ntdll: verified file access (review V2 D1) ----------------------------------

    internal const uint FILE_READ_DATA = 0x0001;
    internal const uint FILE_WRITE_DATA = 0x0002;
    internal const uint FILE_ADD_FILE = 0x0002;
    internal const uint FILE_ADD_SUBDIRECTORY = 0x0004;
    internal const uint FILE_APPEND_DATA = 0x0004;
    internal const uint FILE_TRAVERSE = 0x0020;
    internal const uint FILE_READ_ATTRIBUTES = 0x0080;
    internal const uint FILE_WRITE_ATTRIBUTES = 0x0100;
    internal const uint READ_CONTROL = 0x00020000;
    internal const uint SYNCHRONIZE = 0x00100000;
    internal const uint FILE_SHARE_READ = 0x1;
    internal const uint FILE_SHARE_WRITE = 0x2;
    internal const uint FILE_SHARE_DELETE = 0x4;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    internal const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    internal const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    internal const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

    // NtCreateFile dispositions and options.
    internal const uint FILE_OPEN = 1;
    internal const uint FILE_OPEN_IF = 3;
    internal const uint FILE_DIRECTORY_FILE = 0x00000001;
    internal const uint FILE_SYNCHRONOUS_IO_NONALERT = 0x00000020;
    internal const uint FILE_NON_DIRECTORY_FILE = 0x00000040;
    internal const uint FILE_OPEN_REPARSE_POINT = 0x00200000;
    internal const uint OBJ_CASE_INSENSITIVE = 0x00000040;
    internal const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    internal const int STATUS_OBJECT_NAME_NOT_FOUND = unchecked((int)0xC0000034);
    internal const int STATUS_OBJECT_PATH_NOT_FOUND = unchecked((int)0xC000003A);

    [StructLayout(LayoutKind.Sequential)]
    internal struct UNICODE_STRING
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct OBJECT_ATTRIBUTES
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct IO_STATUS_BLOCK
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BY_HANDLE_FILE_INFORMATION
    {
        internal uint FileAttributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition,
        uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("ntdll.dll")]
    internal static extern int NtCreateFile(
        out Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle, uint desiredAccess, ref OBJECT_ATTRIBUTES objectAttributes,
        out IO_STATUS_BLOCK ioStatusBlock, IntPtr allocationSize, uint fileAttributes, uint shareAccess,
        uint createDisposition, uint createOptions, IntPtr eaBuffer, uint eaLength);

    [DllImport("ntdll.dll")]
    internal static extern uint RtlNtStatusToDosError(int status);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetFinalPathNameByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, [Out] char[] filePath, uint filePathLength, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION information);

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern uint GetLongPathName(string shortPath, [Out] char[]? longPath, uint bufferLength);

    // ---- shell32 -------------------------------------------------------------------------------

    [DllImport("shell32.dll")]
    internal static extern UIntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    // ---- dwmapi --------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetColorizationColor(out uint pcrColorization, [MarshalAs(UnmanagedType.Bool)] out bool pfOpaqueBlend);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    // ---- uxtheme -------------------------------------------------------------------------------

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    // Undocumented ordinals (review S5). Only call them when Core's ThemeCapabilities allows it for the
    // running build: on older builds the ordinal may not exist or may be a different function.

    [DllImport("uxtheme.dll", EntryPoint = "#104")]
    internal static extern void RefreshImmersiveColorPolicyState();

    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowDarkModeForWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool allow);

    /// <summary>Ordinal #135 on build 17763 (1809): <c>BOOL AllowDarkModeForApp(BOOL)</c>.</summary>
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AllowDarkModeForApp([MarshalAs(UnmanagedType.Bool)] bool allow);

    /// <summary>Ordinal #135 from build 18362 (1903): <c>PreferredAppMode SetPreferredAppMode(PreferredAppMode)</c>.</summary>
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    internal static extern int SetPreferredAppMode(int appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    internal static extern void FlushMenuThemes();
}
