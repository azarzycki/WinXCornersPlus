using WinXCorners.Core.Actions;

namespace WinXCorners.App;

/// <summary>Shows the tray menu and the corner action menu as native, theme-aware Win32 popup menus.</summary>
internal static class Win32PopupMenu
{
    internal const int TrayCommandReload = 1000;
    internal const int TrayCommandToggleHotCorners = 1001;
    internal const int TrayCommandHideTray = 1002;
    internal const int TrayCommandElevate = 1003;
    internal const int TrayCommandAdvanced = 1004;
    internal const int TrayCommandLogWindow = 1007;
    internal const int TrayCommandAbout = 1008;
    internal const int TrayCommandExit = 1009;
    internal const int TrayCommandSecurity = 1010;

    /// <summary>Command ids of corner menu items start here (one per non-separator item, in order).</summary>
    private const int FirstCornerCommandId = 2000;

    private const uint TrackFlags =
        NativeMethods.TPM_LEFTALIGN | NativeMethods.TPM_TOPALIGN | NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD;

    /// <summary>The tray menu as data, in display order; separators have id 0. "&amp;&amp;" shows a single "&amp;".</summary>
    /// <param name="restartText">The "Restart" item's text from Core (it says when the restart drops administrator rights).</param>
    internal static IReadOnlyList<(int Id, string Text, bool Enabled, bool Separator)> BuildTrayItems(
        bool hotCornersEnabled, bool isElevated, string restartText)
    {
        ArgumentNullException.ThrowIfNull(restartText);
        return
        [
            (TrayCommandToggleHotCorners, hotCornersEnabled ? "Disable hot corners" : "Enable hot corners", true, false),
            (TrayCommandHideTray, "Hide tray icon", true, false),
            isElevated
                ? (TrayCommandElevate, "Running as administrator", false, false)
                : (TrayCommandElevate, "Restart as administrator…", true, false),
            (0, string.Empty, false, true),
            (TrayCommandReload, restartText.Replace("&", "&&", StringComparison.Ordinal), true, false),
            (TrayCommandLogWindow, "Log window", true, false),
            (TrayCommandAdvanced, "Settings…", true, false),
            (TrayCommandSecurity, "Security && privacy…", true, false),
            (0, string.Empty, false, true),
            (TrayCommandAbout, "About", true, false),
            (TrayCommandExit, "Exit", true, false)
        ];
    }

    /// <summary>Shows the tray menu at <paramref name="screenPoint"/>; returns the chosen command id, or 0.</summary>
    internal static int ShowTrayMenu(IntPtr owner, Point screenPoint, bool hotCornersEnabled, bool isElevated, string restartText)
    {
        var items = BuildTrayItems(hotCornersEnabled, isElevated, restartText);
        return Show(owner, screenPoint, menu =>
        {
            foreach (var item in items)
            {
                Append(menu, item.Separator, item.Enabled, item.Id, item.Text);
            }
        });
    }

    /// <summary>Shows the corner action menu; returns the chosen action id, or null when dismissed.</summary>
    internal static string? ShowCornerMenu(IntPtr owner, Point screenPoint, IReadOnlyList<CornerMenuItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var commands = new Dictionary<int, string>(items.Count);
        var selected = Show(owner, screenPoint, menu =>
        {
            var commandId = FirstCornerCommandId;
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    Append(menu, separator: true, enabled: false, 0, null);
                    continue;
                }

                commands[commandId] = item.ActionId;
                // Custom command names are user text: a single '&' would otherwise become a mnemonic underline.
                Append(menu, separator: false, enabled: true, commandId, item.Text.Replace("&", "&&", StringComparison.Ordinal));
                commandId++;
            }
        });

        return commands.TryGetValue(selected, out var actionId) ? actionId : null;
    }

    private static int Show(IntPtr owner, Point screenPoint, Action<IntPtr> populate)
    {
        ThemeHelper.ApplyNativeWindowTheme(owner);

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            populate(menu);

            // A tray menu only closes on an outside click if its owner is the foreground window, and the WM_NULL
            // afterwards makes the owner process a message so the menu does not reappear stuck (KB135788).
            NativeMethods.SetForegroundWindow(owner);
            var command = NativeMethods.TrackPopupMenuEx(menu, TrackFlags, screenPoint.X, screenPoint.Y, owner, IntPtr.Zero);
            _ = NativeMethods.PostMessage(owner, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void Append(IntPtr menu, bool separator, bool enabled, int id, string? text)
    {
        if (separator)
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, null);
            return;
        }

        var flags = NativeMethods.MF_STRING | (enabled ? 0u : NativeMethods.MF_DISABLED | NativeMethods.MF_GRAYED);
        NativeMethods.AppendMenu(menu, flags, (UIntPtr)(uint)id, text);
    }
}
