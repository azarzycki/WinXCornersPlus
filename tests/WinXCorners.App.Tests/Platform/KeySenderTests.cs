using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Input;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// KeySender.ToInput builds the INPUT records SendInput gets. No keys are actually injected: that would type into
/// whatever has focus on the CI machine.
/// </summary>
public sealed class KeySenderTests
{
    private const ushort VkA = 0x41;
    private const ushort VkRight = 0x27;
    private const ushort VkLeftWin = 0x5B;
    private const ushort VkRightControl = 0xA3;
    private const ushort VkLeftShift = 0xA0;

    [Theory]
    [InlineData(VkA, false, false, 0x1E)]
    [InlineData(VkA, true, false, 0x1E)]
    [InlineData(VkLeftShift, false, false, 0x2A)]
    [InlineData(VkRight, false, true, 0x4D)]
    [InlineData(VkRight, true, true, 0x4D)]
    [InlineData(VkLeftWin, true, true, 0x5B)]
    [InlineData(VkRightControl, false, true, 0x1D)]
    public void ToInput_sets_key_scan_code_and_flags(ushort virtualKey, bool keyUp, bool extended, int scanCode)
    {
        var input = KeySender.ToInput(new KeyEvent(virtualKey, keyUp, extended, DelayAfterMs: 5));

        Assert.Equal(NativeMethods.INPUT_KEYBOARD, input.type);
        Assert.Equal(virtualKey, input.u.ki.wVk);
        Assert.Equal(scanCode, input.u.ki.wScan);
        var expectedFlags = (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0) | (extended ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0);
        Assert.Equal(expectedFlags, input.u.ki.dwFlags);
        Assert.Equal(0u, input.u.ki.time);
        Assert.Equal(UIntPtr.Zero, input.u.ki.dwExtraInfo);
    }

    [Fact]
    public void Core_marks_the_keys_that_need_the_extended_flag()
    {
        Assert.True(VirtualKeys.IsExtended(VkRight));
        Assert.True(VirtualKeys.IsExtended(VkLeftWin));
        Assert.True(VirtualKeys.IsExtended(VkRightControl));
        Assert.False(VirtualKeys.IsExtended(VkA));
        Assert.False(VirtualKeys.IsExtended(VkLeftShift));
    }

    [Fact]
    public void Send_of_nothing_sends_nothing_and_logs_nothing()
    {
        var log = new BoundedLog();
        new KeySender(log).Send([]);
        Assert.Empty(log.Snapshot());
    }

    [Fact]
    public void GetModifiersDown_reports_only_physical_modifier_keys()
    {
        var down = new KeySender(new BoundedLog()).GetModifiersDown();
        Assert.All(down, key => Assert.Contains(key, VirtualKeys.PhysicalModifierKeys));
    }
}
