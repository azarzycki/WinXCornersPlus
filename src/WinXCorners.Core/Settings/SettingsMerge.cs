using WinXCorners.Core.Model;

namespace WinXCorners.Core.Settings;

/// <summary>Applies the result of the Settings dialog to the live settings.</summary>
public static class SettingsMerge
{
    /// <summary>
    /// Copies only the fields the Settings dialog edits from <paramref name="edited"/> into <paramref name="live"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ApplicationSettings.HotCornersEnabled"/> and the four corner action ids are deliberately
    /// left alone: they are changed from the tray menu and flyout while the dialog is open, and copying the
    /// dialog's stale snapshot of them would silently revert those changes (review item B6).
    /// Custom commands are deep-copied so later edits to <paramref name="edited"/> cannot leak into live state.
    /// </remarks>
    public static void ApplyDialogEdits(ApplicationSettings live, ApplicationSettings edited)
    {
        ArgumentNullException.ThrowIfNull(live);
        ArgumentNullException.ThrowIfNull(edited);

        live.GlobalDelayEnabled = edited.GlobalDelayEnabled;
        live.GlobalDelayIndex = edited.GlobalDelayIndex;
        live.TopLeftDelayEnabled = edited.TopLeftDelayEnabled;
        live.TopLeftDelayIndex = edited.TopLeftDelayIndex;
        live.TopRightDelayEnabled = edited.TopRightDelayEnabled;
        live.TopRightDelayIndex = edited.TopRightDelayIndex;
        live.BottomLeftDelayEnabled = edited.BottomLeftDelayEnabled;
        live.BottomLeftDelayIndex = edited.BottomLeftDelayIndex;
        live.BottomRightDelayEnabled = edited.BottomRightDelayEnabled;
        live.BottomRightDelayIndex = edited.BottomRightDelayIndex;
        live.ShowCountdown = edited.ShowCountdown;
        live.StartWithWindows = edited.StartWithWindows;
        live.IgnoreFullScreen = edited.IgnoreFullScreen;
        live.AlwaysRunAsAdministrator = edited.AlwaysRunAsAdministrator;
        live.AlwaysHideTrayIcon = edited.AlwaysHideTrayIcon;
        live.FlyoutAnimationDirection = edited.FlyoutAnimationDirection;
        live.HotCornerModifierKey = edited.HotCornerModifierKey;
        live.EnableCustomCommands = edited.EnableCustomCommands;
        live.CustomCommands = edited.Clone().CustomCommands;
    }
}
