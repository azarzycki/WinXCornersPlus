using WinXCorners.Core.Input;

namespace WinXCorners.Core.Tests.Input;

public sealed class VirtualKeysTests
{
    // Every name from the original HotkeyInvoker table except the mouse buttons, with the WinUser.h code.
    public static TheoryData<string, ushort> OriginalNames => new()
    {
        { "CANCEL", 0x03 }, { "BACK", 0x08 }, { "TAB", 0x09 }, { "CLEAR", 0x0C }, { "RETURN", 0x0D },
        { "SHIFT", 0x10 }, { "CONTROL", 0x11 }, { "ALT", 0x12 }, { "PAUSE", 0x13 }, { "CAPITAL", 0x14 },
        { "ESCAPE", 0x1B }, { "SPACE", 0x20 }, { "PRIOR", 0x21 }, { "NEXT", 0x22 }, { "END", 0x23 },
        { "HOME", 0x24 }, { "LEFT", 0x25 }, { "UP", 0x26 }, { "RIGHT", 0x27 }, { "DOWN", 0x28 },
        { "SELECT", 0x29 }, { "PRINT", 0x2A }, { "EXECUTE", 0x2B }, { "SNAPSHOT", 0x2C }, { "INSERT", 0x2D },
        { "DELETE", 0x2E }, { "HELP", 0x2F }, { "WIN", 0x5B }, { "RWIN", 0x5C }, { "APPS", 0x5D },
        { "SLEEP", 0x5F }, { "NUMPAD0", 0x60 }, { "NUMPAD1", 0x61 }, { "NUMPAD2", 0x62 }, { "NUMPAD3", 0x63 },
        { "NUMPAD4", 0x64 }, { "NUMPAD5", 0x65 }, { "NUMPAD6", 0x66 }, { "NUMPAD7", 0x67 }, { "NUMPAD8", 0x68 },
        { "NUMPAD9", 0x69 }, { "MULTIPLY", 0x6A }, { "ADD", 0x6B }, { "SEPARATOR", 0x6C }, { "SUBTRACT", 0x6D },
        { "DECIMAL", 0x6E }, { "DIVIDE", 0x6F }, { "F1", 0x70 }, { "F2", 0x71 }, { "F3", 0x72 }, { "F4", 0x73 },
        { "F5", 0x74 }, { "F6", 0x75 }, { "F7", 0x76 }, { "F8", 0x77 }, { "F9", 0x78 }, { "F10", 0x79 },
        { "F11", 0x7A }, { "F12", 0x7B }, { "F13", 0x7C }, { "F14", 0x7D }, { "F15", 0x7E }, { "F16", 0x7F },
        { "F17", 0x80 }, { "F18", 0x81 }, { "F19", 0x82 }, { "F20", 0x83 }, { "F21", 0x84 }, { "F22", 0x85 },
        { "F23", 0x86 }, { "F24", 0x87 }, { "NUMLOCK", 0x90 }, { "SCROLL", 0x91 }, { "LSHIFT", 0xA0 },
        { "RSHIFT", 0xA1 }, { "LCONTROL", 0xA2 }, { "RCONTROL", 0xA3 }, { "LMENU", 0xA4 }, { "RMENU", 0xA5 },
        { "BROWSER_BACK", 0xA6 }, { "BROWSER_FORWARD", 0xA7 }, { "BROWSER_REFRESH", 0xA8 },
        { "BROWSER_STOP", 0xA9 }, { "BROWSER_SEARCH", 0xAA }, { "BROWSER_FAVORITES", 0xAB },
        { "BROWSER_HOME", 0xAC }, { "VOLUME_MUTE", 0xAD }, { "VOLUME_DOWN", 0xAE }, { "VOLUME_UP", 0xAF },
        { "MEDIA_NEXT_TRACK", 0xB0 }, { "MEDIA_PREV_TRACK", 0xB1 }, { "MEDIA_STOP", 0xB2 },
        { "MEDIA_PLAY_PAUSE", 0xB3 }, { "LAUNCH_MAIL", 0xB4 }, { "LAUNCH_MEDIA_SELECT", 0xB5 },
        { "LAUNCH_APP1", 0xB6 }, { "LAUNCH_APP2", 0xB7 }, { "OEM_1", 0xBA }, { "OEM_PLUS", 0xBB },
        { "OEM_COMMA", 0xBC }, { "OEM_MINUS", 0xBD }, { "OEM_PERIOD", 0xBE }, { "OEM_2", 0xBF },
        { "OEM_3", 0xC0 }, { "OEM_4", 0xDB }, { "OEM_5", 0xDC }, { "OEM_6", 0xDD }, { "OEM_7", 0xDE },
        { "OEM_8", 0xDF }, { "OEM_102", 0xE2 }, { "PACKET", 0xE7 }, { "PROCESSKEY", 0xE5 }, { "ATTN", 0xF6 },
        { "CRSEL", 0xF7 }, { "EXSEL", 0xF8 }, { "EREOF", 0xF9 }, { "PLAY", 0xFA }, { "ZOOM", 0xFB },
        { "NONAME", 0xFC }, { "PA1", 0xFD }, { "OEM_CLEAR", 0xFE },
    };

    public static TheoryData<string, ushort> Aliases => new()
    {
        { "CTRL", 0x11 }, { "ESC", 0x1B }, { "ENTER", 0x0D }, { "DEL", 0x2E }, { "INS", 0x2D },
        { "PGUP", 0x21 }, { "PAGEUP", 0x21 }, { "PGDN", 0x22 }, { "PAGEDOWN", 0x22 }, { "LWIN", 0x5B },
        { "MENU", 0x12 }, { "LALT", 0xA4 }, { "RALT", 0xA5 }, { "LCTRL", 0xA2 }, { "RCTRL", 0xA3 },
        { "CAPSLOCK", 0x14 }, { "PRINTSCREEN", 0x2C }, { "BACKSPACE", 0x08 }, { "WINDOWS", 0x5B },
    };

    [Theory]
    [MemberData(nameof(OriginalNames))]
    [MemberData(nameof(Aliases))]
    public void TryGet_KnownName_ReturnsWin32CodeInAnyCase(string name, ushort expected)
    {
        Assert.True(VirtualKeys.TryGet(name, out var upper));
        Assert.True(VirtualKeys.TryGet(name.ToLowerInvariant(), out var lower));
        Assert.Equal(expected, upper);
        Assert.Equal(expected, lower);
    }

    [Fact]
    public void TryGet_LettersAndDigits_MapToTheirAsciiCodes()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            Assert.True(VirtualKeys.TryGet(c.ToString(), out var upper));
            Assert.True(VirtualKeys.TryGet(char.ToLowerInvariant(c).ToString(), out var lower));
            Assert.Equal(c, upper);
            Assert.Equal(c, lower);
        }

        for (var c = '0'; c <= '9'; c++)
        {
            Assert.True(VirtualKeys.TryGet(c.ToString(), out var code));
            Assert.Equal(c, code);
        }
    }

    [Theory]
    [InlineData("LBUTTON")]
    [InlineData("rbutton")]
    [InlineData("MBUTTON")]
    [InlineData("XBUTTON1")]
    [InlineData("XBUTTON2")]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("-")]
    [InlineData("é")]
    [InlineData(" A")]
    [InlineData("F25")]
    [InlineData("CONTROL ")]
    public void TryGet_UnknownOrMouseName_Fails(string name)
    {
        Assert.False(VirtualKeys.TryGet(name, out var code));
        Assert.Equal(0, code);
    }

    [Fact]
    public void TryGet_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VirtualKeys.TryGet(null!, out _));
    }

    [Theory]
    [InlineData("RCONTROL")]
    [InlineData("RMENU")]
    [InlineData("INSERT")]
    [InlineData("DELETE")]
    [InlineData("HOME")]
    [InlineData("END")]
    [InlineData("PRIOR")]
    [InlineData("NEXT")]
    [InlineData("LEFT")]
    [InlineData("UP")]
    [InlineData("RIGHT")]
    [InlineData("DOWN")]
    [InlineData("NUMLOCK")]
    [InlineData("SNAPSHOT")]
    [InlineData("DIVIDE")]
    [InlineData("LWIN")]
    [InlineData("RWIN")]
    [InlineData("APPS")]
    public void IsExtended_ExtendedKeys_True(string name)
    {
        Assert.True(VirtualKeys.TryGet(name, out var code));
        Assert.True(VirtualKeys.IsExtended(code));
    }

    [Fact]
    public void IsExtended_BrowserVolumeMediaLaunchRange_TrueExactlyFromA6ToB7()
    {
        Assert.False(VirtualKeys.IsExtended(0xA4)); // LMENU
        for (ushort code = 0xA6; code <= 0xB7; code++)
        {
            Assert.True(VirtualKeys.IsExtended(code), $"0x{code:X2}");
        }

        Assert.False(VirtualKeys.IsExtended(0xB8));
        Assert.False(VirtualKeys.IsExtended(0xBA));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("SHIFT")]
    [InlineData("CONTROL")]
    [InlineData("ALT")]
    [InlineData("LCONTROL")]
    [InlineData("LMENU")]
    [InlineData("LSHIFT")]
    [InlineData("RSHIFT")]
    [InlineData("NUMPAD7")]
    [InlineData("TAB")]
    [InlineData("ESCAPE")]
    [InlineData("RETURN")]
    public void IsExtended_OrdinaryKeys_False(string name)
    {
        Assert.True(VirtualKeys.TryGet(name, out var code));
        Assert.False(VirtualKeys.IsExtended(code));
    }

    [Theory]
    [InlineData("SHIFT")]
    [InlineData("CONTROL")]
    [InlineData("ALT")]
    [InlineData("LSHIFT")]
    [InlineData("RSHIFT")]
    [InlineData("LCONTROL")]
    [InlineData("RCONTROL")]
    [InlineData("LMENU")]
    [InlineData("RMENU")]
    [InlineData("WIN")]
    [InlineData("RWIN")]
    public void IsModifier_ModifierKeys_True(string name)
    {
        Assert.True(VirtualKeys.TryGet(name, out var code));
        Assert.True(VirtualKeys.IsModifier(code));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("TAB")]
    [InlineData("CAPITAL")]
    [InlineData("APPS")]
    [InlineData("HOME")]
    public void IsModifier_OtherKeys_False(string name)
    {
        Assert.True(VirtualKeys.TryGet(name, out var code));
        Assert.False(VirtualKeys.IsModifier(code));
    }

    [Fact]
    public void PhysicalModifierKeys_AreTheEightSideSpecificModifiers()
    {
        Assert.Equal(new ushort[] { 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C }, VirtualKeys.PhysicalModifierKeys);
        Assert.All(VirtualKeys.PhysicalModifierKeys, code => Assert.True(VirtualKeys.IsModifier(code)));
    }
}
