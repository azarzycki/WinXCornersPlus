using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace WinXCorners.App;

/// <summary>
/// Starts a program from an elevated process WITHOUT administrator rights, by asking the desktop shell
/// (explorer.exe, which runs with the user's normal token) to perform the ShellExecute (brief design point 1).
/// </summary>
/// <remarks>
/// <para>This is the technique Raymond Chen documents in "How can I launch an unelevated process from my elevated
/// process and vice versa?": find the desktop's shell view through <c>IShellWindows</c>, get its background
/// automation object, take its <c>Application</c> (the shell's own <c>IShellDispatch2</c>) and call
/// <c>ShellExecute</c> on it. The child is created by explorer, so it gets explorer's token.</para>
/// <para>There is deliberately no fallback to <c>Process.Start</c>: that would run the child with our elevated
/// token, which is exactly what this class exists to prevent. Failing closed is the point.</para>
/// <para>The technique only helps when the shell itself is not elevated (review V2 D4): with User Account Control
/// off, on the built-in Administrator account, or when someone started explorer.exe as administrator, the shell's
/// token is elevated too. So the shell process's own token is checked (TokenElevation) before every launch, and an
/// elevated or unreadable shell token fails the launch.</para>
/// <para>Call from an STA thread (the UI thread or the action worker). Explorer reports problems with the target
/// itself (e.g. file not found) in its own dialog; this class can only report failures to reach the shell.</para>
/// </remarks>
internal static class DeElevatedLauncher
{
    private const int CsidlDesktop = 0;
    private const int SwcDesktop = 8;
    private const int SwfoNeedDispatch = 1;
    private const int SvgioBackground = 0;
    private const int SwHide = 0;
    private const int SwShowNormal = 1;

    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidSTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IidIShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IidIDispatch = new("00020400-0000-0000-C000-000000000046");

    /// <summary>
    /// ShellExecutes <paramref name="file"/> inside explorer.exe. Returns false with a message when the shell could
    /// not be reached; never throws for that and never launches with this process's token.
    /// </summary>
    internal static bool TryShellExecute(string file, string arguments, string workingDirectory, bool hidden, out string? error)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            error = "Nothing to start: the file name is empty.";
            return false;
        }

        var comObjects = new List<object>();
        try
        {
            var shellWindowsType = Type.GetTypeFromCLSID(ClsidShellWindows, throwOnError: false);
            if (shellWindowsType is null)
            {
                error = "The desktop shell's window list is not available.";
                return false;
            }

            var shellWindowsObject = Activator.CreateInstance(shellWindowsType);
            if (shellWindowsObject is null)
            {
                error = "The desktop shell's window list could not be created.";
                return false;
            }

            comObjects.Add(shellWindowsObject);
            var shellWindows = (IShellWindows)shellWindowsObject;

            object location = CsidlDesktop;
            object? emptyRoot = null; // marshals as VT_EMPTY
            var hr = shellWindows.FindWindowSW(ref location, ref emptyRoot, SwcDesktop, out var desktopWindow, SwfoNeedDispatch, out var desktopDispatch);
            if (desktopDispatch is not null)
            {
                comObjects.Add(desktopDispatch);
            }

            if (hr != 0 || desktopDispatch is null)
            {
                error = "The desktop shell (explorer.exe) is not running or has not registered the desktop " +
                        $"(0x{hr:X8}), so the program cannot be started without administrator rights.";
                return false;
            }

            // The process that registered the desktop view is the one that will perform the ShellExecute.
            var shellCheck = CheckShellToken(new IntPtr(desktopWindow));
            if (shellCheck is not null)
            {
                error = shellCheck;
                return false;
            }

            var serviceProvider = (IComServiceProvider)desktopDispatch;
            var service = SidSTopLevelBrowser;
            var iidBrowser = IidIShellBrowser;
            hr = serviceProvider.QueryService(ref service, ref iidBrowser, out var browserObject);
            if (browserObject is not null)
            {
                comObjects.Add(browserObject);
            }

            if (hr != 0 || browserObject is null)
            {
                error = $"The desktop shell did not provide its browser (0x{hr:X8}).";
                return false;
            }

            hr = ((IShellBrowser)browserObject).QueryActiveShellView(out var viewObject);
            if (viewObject is not null)
            {
                comObjects.Add(viewObject);
            }

            if (hr != 0 || viewObject is null)
            {
                error = $"The desktop shell did not provide its view (0x{hr:X8}).";
                return false;
            }

            var iidDispatch = IidIDispatch;
            hr = ((IShellView)viewObject).GetItemObject(SvgioBackground, ref iidDispatch, out var folderViewObject);
            if (folderViewObject is not null)
            {
                comObjects.Add(folderViewObject);
            }

            if (hr != 0 || folderViewObject is null)
            {
                error = $"The desktop shell did not provide its automation object (0x{hr:X8}).";
                return false;
            }

            // IShellFolderViewDual.Application and IShellDispatch2.ShellExecute are IDispatch members; late binding
            // by name avoids hand-maintained DISPIDs.
            dynamic folderView = folderViewObject;
            object? application = folderView.Application;
            if (application is null)
            {
                error = "The desktop shell did not provide its Application object.";
                return false;
            }

            comObjects.Add(application);
            dynamic shell = application;
            shell.ShellExecute(file, arguments ?? string.Empty, workingDirectory ?? string.Empty, "open", hidden ? SwHide : SwShowNormal);
            error = null;
            return true;
        }
        catch (COMException ex)
        {
            error = $"The desktop shell refused the request (0x{ex.HResult:X8}): {ex.Message}";
            return false;
        }
        catch (InvalidCastException ex)
        {
            error = $"The desktop shell does not support the required interface: {ex.Message}";
            return false;
        }
        catch (InvalidComObjectException ex)
        {
            error = $"The desktop shell went away during the request: {ex.Message}";
            return false;
        }
        catch (RuntimeBinderException ex)
        {
            error = $"The desktop shell's automation object is not as expected: {ex.Message}";
            return false;
        }
        catch (TargetInvocationException ex)
        {
            error = $"The desktop shell refused the request: {ex.InnerException?.Message ?? ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = $"Access to the desktop shell was denied: {ex.Message}";
            return false;
        }
        catch (NotSupportedException ex)
        {
            error = $"COM is not available: {ex.Message}";
            return false;
        }
        finally
        {
            ReleaseAll(comObjects);
        }
    }

    /// <summary>
    /// Null when the desktop shell that owns <paramref name="desktopWindow"/> (or, when that is zero, the shell window)
    /// runs without administrator rights; otherwise why launching through it would not drop them.
    /// </summary>
    internal static string? CheckShellToken(IntPtr desktopWindow)
    {
        var window = desktopWindow != IntPtr.Zero ? desktopWindow : NativeMethods.GetShellWindow();
        if (window == IntPtr.Zero)
        {
            return "The desktop shell has no window, so its rights cannot be checked; nothing was started.";
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return ProcessElevation.IsProcessElevated((int)processId) switch
        {
            false => null,
            true => "The desktop shell (explorer.exe) itself runs as administrator, so a program started through it " +
                    "would get administrator rights too; nothing was started.",
            null => "The desktop shell's rights could not be checked, so nothing was started."
        };
    }

    private static void ReleaseAll(List<object> comObjects)
    {
        // A cast of an RCW to an interface returns the same object, so release each distinct wrapper once.
        var released = new List<object>(comObjects.Count);
        for (var i = comObjects.Count - 1; i >= 0; i--)
        {
            var candidate = comObjects[i];
            if (released.Exists(r => ReferenceEquals(r, candidate)) || !Marshal.IsComObject(candidate))
            {
                continue;
            }

            released.Add(candidate);
            Marshal.FinalReleaseComObject(candidate);
        }
    }

    // ---- COM declarations ------------------------------------------------------------------------
    // Each interface is declared as IUnknown-based with explicit placeholders for every vtable slot before the one
    // we call, so the slot numbers are visible and checkable against the SDK headers. Placeholders are never called.

    /// <summary>IShellWindows (exdisp.h). Dual interface: IDispatch's four slots come first.</summary>
    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void IDispatchGetTypeInfoCount();
        void IDispatchGetTypeInfo();
        void IDispatchGetIDsOfNames();
        void IDispatchInvoke();
        void GetCount();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [PreserveSig]
        int FindWindowSW(
            [In] ref object pvarLoc,
            [In] ref object? pvarLocRoot,
            int swClass,
            out int phwnd,
            int swfwOptions,
            [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
    }

    /// <summary>IServiceProvider (servprov.h); named so as not to clash with System.IServiceProvider.</summary>
    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
    }

    /// <summary>IShellBrowser (shobjidl_core.h): IOleWindow's 2 slots, 10 of its own, then QueryActiveShellView.</summary>
    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        [PreserveSig]
        int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? ppshv);
    }

    /// <summary>IShellView (shobjidl_core.h): IOleWindow's 2 slots, 10 of its own, then GetItemObject.</summary>
    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();

        [PreserveSig]
        int GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    }
}
