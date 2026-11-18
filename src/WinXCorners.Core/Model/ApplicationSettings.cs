namespace WinXCorners.Core.Model;

public enum FlyoutAnimationDirection
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>Modifier that must be held for a hot corner to fire. Stored as a number in settings.json.</summary>
public enum HotCornerModifierKey
{
    None,
    Ctrl,
    Shift,
    Alt
}

/// <summary>
/// The persisted user configuration. Property names are the settings.json contract and must not be
/// renamed. Instances read from disk are untrusted until they have been through the sanitizer.
/// </summary>
public sealed class ApplicationSettings
{
    public const int CustomCommandCount = 4;
    public const int DelayOptionCount = 6;
    public const int DefaultDelayIndex = 3;

    public const string DefaultTopLeftActionId = "file-explorer";
    public const string DefaultTopRightActionId = "hide-other-windows";
    public const string DefaultBottomLeftActionId = "all-windows";
    public const string DefaultBottomRightActionId = "action-center";

    public bool HotCornersEnabled { get; set; } = true;

    public string TopLeftActionId { get; set; } = DefaultTopLeftActionId;

    public string TopRightActionId { get; set; } = DefaultTopRightActionId;

    public string BottomLeftActionId { get; set; } = DefaultBottomLeftActionId;

    public string BottomRightActionId { get; set; } = DefaultBottomRightActionId;

    /// <summary>Mirrors the HKCU Run value. The registry, not this file, is the source of truth.</summary>
    public bool StartWithWindows { get; set; }

    public bool IgnoreFullScreen { get; set; } = true;

    public bool AlwaysRunAsAdministrator { get; set; }

    public bool AlwaysHideTrayIcon { get; set; }

    public FlyoutAnimationDirection FlyoutAnimationDirection { get; set; } = FlyoutAnimationDirection.Bottom;

    public bool GlobalDelayEnabled { get; set; }

    public int GlobalDelayIndex { get; set; } = DefaultDelayIndex;

    public bool TopLeftDelayEnabled { get; set; }

    public int TopLeftDelayIndex { get; set; } = DefaultDelayIndex;

    public bool TopRightDelayEnabled { get; set; }

    public int TopRightDelayIndex { get; set; } = DefaultDelayIndex;

    public bool BottomLeftDelayEnabled { get; set; }

    public int BottomLeftDelayIndex { get; set; } = DefaultDelayIndex;

    public bool BottomRightDelayEnabled { get; set; }

    public int BottomRightDelayIndex { get; set; } = DefaultDelayIndex;

    public bool ShowCountdown { get; set; }

    public bool EnableCustomCommands { get; set; }

    public CustomCommandSettings[] CustomCommands { get; set; } = CreateDefaultCommands();

    public HotCornerModifierKey HotCornerModifierKey { get; set; } = HotCornerModifierKey.None;

    /// <summary>Deep copy. Tolerates a null array or null elements so it is safe on unsanitized input.</summary>
    public ApplicationSettings Clone()
    {
        return new ApplicationSettings
        {
            HotCornersEnabled = HotCornersEnabled,
            TopLeftActionId = TopLeftActionId,
            TopRightActionId = TopRightActionId,
            BottomLeftActionId = BottomLeftActionId,
            BottomRightActionId = BottomRightActionId,
            StartWithWindows = StartWithWindows,
            IgnoreFullScreen = IgnoreFullScreen,
            AlwaysRunAsAdministrator = AlwaysRunAsAdministrator,
            AlwaysHideTrayIcon = AlwaysHideTrayIcon,
            FlyoutAnimationDirection = FlyoutAnimationDirection,
            GlobalDelayEnabled = GlobalDelayEnabled,
            GlobalDelayIndex = GlobalDelayIndex,
            TopLeftDelayEnabled = TopLeftDelayEnabled,
            TopLeftDelayIndex = TopLeftDelayIndex,
            TopRightDelayEnabled = TopRightDelayEnabled,
            TopRightDelayIndex = TopRightDelayIndex,
            BottomLeftDelayEnabled = BottomLeftDelayEnabled,
            BottomLeftDelayIndex = BottomLeftDelayIndex,
            BottomRightDelayEnabled = BottomRightDelayEnabled,
            BottomRightDelayIndex = BottomRightDelayIndex,
            ShowCountdown = ShowCountdown,
            EnableCustomCommands = EnableCustomCommands,
            CustomCommands = (CustomCommands ?? [])
                .Select(static command => command?.Clone() ?? new CustomCommandSettings())
                .ToArray(),
            HotCornerModifierKey = HotCornerModifierKey
        };
    }

    public static ApplicationSettings CreateDefault() => new();

    public static CustomCommandSettings[] CreateDefaultCommands()
    {
        return Enumerable.Range(0, CustomCommandCount)
            .Select(static _ => new CustomCommandSettings())
            .ToArray();
    }
}

public sealed class CustomCommandSettings
{
    public string Name { get; set; } = string.Empty;

    /// <summary>A program/document/URL to open, or a hotkey expression starting with '!', '#' or '@'.</summary>
    public string Command { get; set; } = string.Empty;

    public string Parameters { get; set; } = string.Empty;

    public bool LaunchHidden { get; set; }

    public CustomCommandSettings Clone()
    {
        return new CustomCommandSettings
        {
            Name = Name ?? string.Empty,
            Command = Command ?? string.Empty,
            Parameters = Parameters ?? string.Empty,
            LaunchHidden = LaunchHidden
        };
    }
}
