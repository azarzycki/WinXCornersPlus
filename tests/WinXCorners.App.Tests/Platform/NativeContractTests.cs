using System.Drawing;
using System.Runtime.InteropServices;
using WinXCorners.App.Tests.Infrastructure;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>The P/Invoke structs must have exactly the native layout, or SendInput/SHAppBarMessage reject them.</summary>
public sealed class NativeContractTests
{
    private readonly ITestOutputHelper _output;

    public NativeContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Struct_sizes_match_the_Windows_SDK_for_this_process_bitness()
    {
        _output.WriteLine(TestEnvironment.Describe());
        var x64 = IntPtr.Size == 8;
        Assert.Equal(16, Marshal.SizeOf<NativeMethods.RECT>());
        Assert.Equal(40, Marshal.SizeOf<NativeMethods.MONITORINFO>());
        Assert.Equal(x64 ? 24 : 16, Marshal.SizeOf<NativeMethods.KEYBDINPUT>());
        Assert.Equal(x64 ? 32 : 24, Marshal.SizeOf<NativeMethods.MOUSEINPUT>());
        Assert.Equal(x64 ? 40 : 28, Marshal.SizeOf<NativeMethods.INPUT>());
        Assert.Equal(x64 ? 48 : 36, Marshal.SizeOf<NativeMethods.APPBARDATA>());
        Assert.Equal(x64 ? 16 : 12, Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
    }

    [Fact]
    public void Input_union_starts_at_the_pointer_aligned_offset()
    {
        Assert.Equal(IntPtr.Size == 8 ? 8 : 4, Marshal.OffsetOf<NativeMethods.INPUT>(nameof(NativeMethods.INPUT.u)).ToInt32());
        Assert.Equal(0, Marshal.OffsetOf<NativeMethods.InputUnion>(nameof(NativeMethods.InputUnion.ki)).ToInt32());
    }

    [Fact]
    public void GetWindowLongPtr_reads_the_styles_of_a_real_window()
    {
        Sta.Run(() =>
        {
            using var window = TestWindow.Create(
                TestNative.WS_OVERLAPPEDWINDOW, TestNative.WS_EX_TOOLWINDOW, new Rectangle(10, 10, 300, 200));

            var style = NativeMethods.GetWindowLongPtr(window.Handle, NativeMethods.GWL_STYLE).ToInt64();
            var exStyle = NativeMethods.GetWindowLongPtr(window.Handle, NativeMethods.GWL_EXSTYLE).ToInt64();

            Assert.Equal(NativeMethods.WS_CAPTION, style & NativeMethods.WS_CAPTION);
            Assert.NotEqual(0, exStyle & TestNative.WS_EX_TOOLWINDOW);
            Assert.Equal(0, exStyle & NativeMethods.WS_EX_TRANSPARENT);
        });
    }

    [Fact]
    public void GetWindowLongPtr_on_a_destroyed_window_returns_zero_without_throwing()
    {
        var handle = Sta.Run(() =>
        {
            using var window = TestWindow.Create(TestNative.WS_OVERLAPPEDWINDOW, 0, new Rectangle(0, 0, 100, 100));
            return window.Handle;
        });

        Assert.Equal(IntPtr.Zero, NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_STYLE));
    }
}
