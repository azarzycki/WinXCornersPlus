using System.Collections.Frozen;
using System.Collections.Immutable;

namespace WinXCorners.Core.Input;

/// <summary>
/// Maps the key names users type in custom hotkeys (for example <c>control</c>, <c>win</c>, <c>f5</c>)
/// to Win32 virtual-key codes, and classifies codes the way <c>SendInput</c> needs.
/// </summary>
public static class VirtualKeys
{
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkLWin = 0x5B;
    private const ushort VkRWin = 0x5C;
    private const ushort VkLShift = 0xA0;
    private const ushort VkRShift = 0xA1;
    private const ushort VkLControl = 0xA2;
    private const ushort VkRControl = 0xA3;
    private const ushort VkLMenu = 0xA4;
    private const ushort VkRMenu = 0xA5;

    // The names from the original HotkeyInvoker table (minus mouse buttons) plus friendlier aliases.
    // Codes follow the Win32 VK_* constants in WinUser.h.
    private static readonly FrozenDictionary<string, ushort> Names = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        ["CANCEL"] = 0x03,
        ["BACK"] = 0x08,
        ["TAB"] = 0x09,
        ["CLEAR"] = 0x0C,
        ["RETURN"] = 0x0D,
        ["SHIFT"] = VkShift,
        ["CONTROL"] = VkControl,
        ["ALT"] = VkMenu,
        ["PAUSE"] = 0x13,
        ["CAPITAL"] = 0x14,
        ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20,
        ["PRIOR"] = 0x21,
        ["NEXT"] = 0x22,
        ["END"] = 0x23,
        ["HOME"] = 0x24,
        ["LEFT"] = 0x25,
        ["UP"] = 0x26,
        ["RIGHT"] = 0x27,
        ["DOWN"] = 0x28,
        ["SELECT"] = 0x29,
        ["PRINT"] = 0x2A,
        ["EXECUTE"] = 0x2B,
        ["SNAPSHOT"] = 0x2C,
        ["INSERT"] = 0x2D,
        ["DELETE"] = 0x2E,
        ["HELP"] = 0x2F,
        ["WIN"] = VkLWin,
        ["RWIN"] = VkRWin,
        ["APPS"] = 0x5D,
        ["SLEEP"] = 0x5F,
        ["NUMPAD0"] = 0x60,
        ["NUMPAD1"] = 0x61,
        ["NUMPAD2"] = 0x62,
        ["NUMPAD3"] = 0x63,
        ["NUMPAD4"] = 0x64,
        ["NUMPAD5"] = 0x65,
        ["NUMPAD6"] = 0x66,
        ["NUMPAD7"] = 0x67,
        ["NUMPAD8"] = 0x68,
        ["NUMPAD9"] = 0x69,
        ["MULTIPLY"] = 0x6A,
        ["ADD"] = 0x6B,
        ["SEPARATOR"] = 0x6C,
        ["SUBTRACT"] = 0x6D,
        ["DECIMAL"] = 0x6E,
        ["DIVIDE"] = 0x6F,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B,
        ["F13"] = 0x7C,
        ["F14"] = 0x7D,
        ["F15"] = 0x7E,
        ["F16"] = 0x7F,
        ["F17"] = 0x80,
        ["F18"] = 0x81,
        ["F19"] = 0x82,
        ["F20"] = 0x83,
        ["F21"] = 0x84,
        ["F22"] = 0x85,
        ["F23"] = 0x86,
        ["F24"] = 0x87,
        ["NUMLOCK"] = 0x90,
        ["SCROLL"] = 0x91,
        ["LSHIFT"] = VkLShift,
        ["RSHIFT"] = VkRShift,
        ["LCONTROL"] = VkLControl,
        ["RCONTROL"] = VkRControl,
        ["LMENU"] = VkLMenu,
        ["RMENU"] = VkRMenu,
        ["BROWSER_BACK"] = 0xA6,
        ["BROWSER_FORWARD"] = 0xA7,
        ["BROWSER_REFRESH"] = 0xA8,
        ["BROWSER_STOP"] = 0xA9,
        ["BROWSER_SEARCH"] = 0xAA,
        ["BROWSER_FAVORITES"] = 0xAB,
        ["BROWSER_HOME"] = 0xAC,
        ["VOLUME_MUTE"] = 0xAD,
        ["VOLUME_DOWN"] = 0xAE,
        ["VOLUME_UP"] = 0xAF,
        ["MEDIA_NEXT_TRACK"] = 0xB0,
        ["MEDIA_PREV_TRACK"] = 0xB1,
        ["MEDIA_STOP"] = 0xB2,
        ["MEDIA_PLAY_PAUSE"] = 0xB3,
        ["LAUNCH_MAIL"] = 0xB4,
        ["LAUNCH_MEDIA_SELECT"] = 0xB5,
        ["LAUNCH_APP1"] = 0xB6,
        ["LAUNCH_APP2"] = 0xB7,
        ["OEM_1"] = 0xBA,
        ["OEM_PLUS"] = 0xBB,
        ["OEM_COMMA"] = 0xBC,
        ["OEM_MINUS"] = 0xBD,
        ["OEM_PERIOD"] = 0xBE,
        ["OEM_2"] = 0xBF,
        ["OEM_3"] = 0xC0,
        ["OEM_4"] = 0xDB,
        ["OEM_5"] = 0xDC,
        ["OEM_6"] = 0xDD,
        ["OEM_7"] = 0xDE,
        ["OEM_8"] = 0xDF,
        ["OEM_102"] = 0xE2,
        ["PROCESSKEY"] = 0xE5,
        ["PACKET"] = 0xE7,
        ["ATTN"] = 0xF6,
        ["CRSEL"] = 0xF7,
        ["EXSEL"] = 0xF8,
        ["EREOF"] = 0xF9,
        ["PLAY"] = 0xFA,
        ["ZOOM"] = 0xFB,
        ["NONAME"] = 0xFC,
        ["PA1"] = 0xFD,
        ["OEM_CLEAR"] = 0xFE,

        // Aliases.
        ["CTRL"] = VkControl,
        ["ESC"] = 0x1B,
        ["ENTER"] = 0x0D,
        ["DEL"] = 0x2E,
        ["INS"] = 0x2D,
        ["PGUP"] = 0x21,
        ["PAGEUP"] = 0x21,
        ["PGDN"] = 0x22,
        ["PAGEDOWN"] = 0x22,
        ["LWIN"] = VkLWin,
        ["WINDOWS"] = VkLWin,
        ["MENU"] = VkMenu,
        ["LALT"] = VkLMenu,
        ["RALT"] = VkRMenu,
        ["LCTRL"] = VkLControl,
        ["RCTRL"] = VkRControl,
        ["CAPSLOCK"] = 0x14,
        ["PRINTSCREEN"] = 0x2C,
        ["BACKSPACE"] = 0x08,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Mouse buttons were in the original table but keyboard injection cannot click, so they
    // silently did nothing. They are rejected with an explanation instead.
    private static readonly FrozenSet<string> MouseButtonNames =
        new[] { "LBUTTON", "RBUTTON", "MBUTTON", "XBUTTON1", "XBUTTON2" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Keys whose scan code is prefixed with 0xE0 on a real keyboard. Without KEYEVENTF_EXTENDEDKEY,
    // Windows treats e.g. HOME as numpad 7 when NumLock is off, and RCONTROL as LCONTROL.
    private static readonly FrozenSet<ushort> ExtendedKeys = new ushort[]
    {
        VkRControl, VkRMenu,
        0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, // INSERT, DELETE, HOME, END, PRIOR, NEXT
        0x25, 0x26, 0x27, 0x28,             // arrows
        0x90, 0x2C, 0x6F,                   // NUMLOCK, SNAPSHOT, DIVIDE
        VkLWin, VkRWin, 0x5D,               // LWIN, RWIN, APPS
    }.Concat(Enumerable.Range(0xA6, 0xB7 - 0xA6 + 1).Select(static code => (ushort)code)).ToFrozenSet();

    private static readonly ImmutableArray<ushort> PhysicalModifiers =
        [VkLShift, VkRShift, VkLControl, VkRControl, VkLMenu, VkRMenu, VkLWin, VkRWin];

    /// <summary>
    /// The side-specific modifier keys the app polls (GetAsyncKeyState) to learn what the user is holding.
    /// </summary>
    public static IReadOnlyList<ushort> PhysicalModifierKeys => PhysicalModifiers;

    /// <summary>
    /// Looks up a key name, ignoring case. Accepts the names listed in the README's key table, the
    /// aliases (CTRL, ESC, ...), single letters A-Z and single digits 0-9.
    /// </summary>
    public static bool TryGet(string name, out ushort virtualKey)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Names.TryGetValue(name, out virtualKey))
        {
            return true;
        }

        if (name.Length == 1)
        {
            var character = char.ToUpperInvariant(name[0]);
            // VK codes for letters and digits equal their upper-case ASCII values.
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        virtualKey = 0;
        return false;
    }

    /// <summary>True when the key must be injected with KEYEVENTF_EXTENDEDKEY.</summary>
    public static bool IsExtended(ushort virtualKey) => ExtendedKeys.Contains(virtualKey);

    /// <summary>
    /// True for Shift, Ctrl, Alt and Windows keys, both the generic codes (SHIFT, CONTROL, ALT) and the
    /// side-specific ones in <see cref="PhysicalModifierKeys"/>.
    /// </summary>
    public static bool IsModifier(ushort virtualKey) =>
        virtualKey is VkShift or VkControl or VkMenu || PhysicalModifiers.Contains(virtualKey);

    /// <summary>True for the names of mouse buttons that the original key table accepted but never worked.</summary>
    internal static bool IsMouseButtonName(string name) => MouseButtonNames.Contains(name);
}
