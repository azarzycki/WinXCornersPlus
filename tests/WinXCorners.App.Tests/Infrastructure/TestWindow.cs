using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;

namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>
/// A real top-level window with a unique, test-owned class name and title (DefWindowProc as its window procedure).
/// Create, use and dispose it on one STA thread (<see cref="Sta.Run(Action, TimeSpan?)"/>).
/// </summary>
internal sealed class TestWindow : IDisposable
{
    private static readonly IntPtr DefWindowProc =
        TestNative.GetProcAddress(TestNative.GetModuleHandle("user32.dll"), "DefWindowProcW");

    private readonly IntPtr _instance;

    private TestWindow(IntPtr handle, string className, string title, IntPtr instance)
    {
        Handle = handle;
        ClassName = className;
        Title = title;
        _instance = instance;
    }

    internal IntPtr Handle { get; private set; }

    internal string ClassName { get; }

    internal string Title { get; }

    internal static TestWindow Create(uint style, uint exStyle, Rectangle bounds, string? title = null)
    {
        var id = Guid.NewGuid().ToString("N");
        var className = "WXC.TestWindow." + id;
        var windowTitle = title ?? "WinXCorners test window " + id;
        var instance = TestNative.GetModuleHandle(null);
        var windowClass = new TestNative.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<TestNative.WNDCLASSEXW>(),
            lpfnWndProc = DefWindowProc,
            hInstance = instance,
            lpszClassName = className
        };
        if (TestNative.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "RegisterClassEx failed");
        }

        var handle = TestNative.CreateWindowEx(
            exStyle, className, windowTitle, style, bounds.X, bounds.Y, bounds.Width, bounds.Height,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            TestNative.UnregisterClass(className, instance);
            throw new Win32Exception(error, "CreateWindowEx failed");
        }

        return new TestWindow(handle, className, windowTitle, instance);
    }

    /// <summary>Tries to make this window the foreground window; true when Windows allowed it.</summary>
    internal bool TryBringToForeground()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            TestNative.ShowWindow(Handle, TestNative.SW_SHOW);
            TestNative.BringWindowToTop(Handle);
            TestNative.SetForegroundWindow(Handle);
            Sta.Pump(TimeSpan.FromMilliseconds(100));
            if (TestNative.GetForegroundWindow() == Handle)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            TestNative.DestroyWindow(Handle);
            Handle = IntPtr.Zero;
            TestNative.UnregisterClass(ClassName, _instance);
        }
    }
}
