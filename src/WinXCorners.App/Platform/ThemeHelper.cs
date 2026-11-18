using WinXCorners.Core.Layout;
using System.Security;
using Microsoft.Win32;
using WinXCorners.Core.Platform;

namespace WinXCorners.App;

/// <summary>Windows version facts used by the themed UI.</summary>
internal static class SystemInfoHelper
{
    internal static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
}

/// <summary>
/// Light/dark theme detection, the app's colour palette, its fonts, and dark mode for native menus and windows.
/// </summary>
/// <remarks>
/// <para>This is the one class with static mutable state, and it is a cache only: the light/dark flag and the accent
/// colour are read once (the original read the registry on every colour lookup, review B8) until
/// <see cref="Invalidate"/> is called. The main form calls it on WM_SETTINGCHANGE, WM_THEMECHANGED and
/// WM_DWMCOLORIZATIONCOLORCHANGED. Fonts are created once and shared; callers must not dispose them.</para>
/// <para>The undocumented uxtheme ordinals are called only as far as Core's <see cref="ThemeCapabilities"/> allows
/// for the running build (review S5): on older builds the ordinals may be missing or be other functions.</para>
/// </remarks>
internal static class ThemeHelper
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Color DefaultAccentColor = Color.FromArgb(0, 120, 215);
    private static readonly Lock Sync = new();
    private static readonly ThemeCapabilities Capabilities = ThemeCapabilities.ForWindowsBuild(Environment.OSVersion.Version.Build);

    private static readonly Lazy<Font> FlyoutButtonFont = new(static () => SystemInfoHelper.IsWindows11
        ? new Font("Segoe UI Variable Text", 9F, FontStyle.Regular, GraphicsUnit.Point)
        : new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point));

    private static readonly Lazy<Font> FlyoutNumberFont = new(static () => SystemInfoHelper.IsWindows11
        ? new Font("Segoe UI Variable Text", 22F, FontStyle.Regular, GraphicsUnit.Point)
        : new Font("Segoe UI", 23F, FontStyle.Regular, GraphicsUnit.Point));

    private static readonly Lazy<Font> CountdownFont = new(static () => SystemInfoHelper.IsWindows11
        ? new Font("Segoe UI Variable Text Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point)
        : new Font("Segoe UI Semibold", 10F, FontStyle.Regular, GraphicsUnit.Point));

    // ---- cache (guarded by Sync) ----
    private static bool? _isLightTheme;
    private static Color? _accentColor;

    // Set once a dark-mode export turns out to be missing, so it is not retried on every menu.
    private static volatile bool _darkModeApisUnavailable;

    private enum PreferredAppMode
    {
        Default = 0,
        AllowDark = 1,
        ForceDark = 2,
        ForceLight = 3
    }

    /// <summary>Apps use the light theme (cached until <see cref="Invalidate"/>). Dark when it cannot be read.</summary>
    internal static bool IsLightTheme
    {
        get
        {
            lock (Sync)
            {
                return _isLightTheme ??= ReadIsLightTheme();
            }
        }
    }

    /// <summary>Forgets the cached theme flag and accent colour; the next lookup reads them again.</summary>
    internal static void Invalidate()
    {
        lock (Sync)
        {
            _isLightTheme = null;
            _accentColor = null;
        }
    }

    /// <summary>Makes native popup menus follow the light/dark theme, where the running build supports it.</summary>
    internal static void ApplyNativeMenuTheme()
    {
        if (!Capabilities.SupportsDarkModeApis || _darkModeApisUnavailable)
        {
            return;
        }

        var light = IsLightTheme;
        try
        {
            if (Capabilities.UsesSetPreferredAppMode)
            {
                NativeMethods.SetPreferredAppMode((int)(light ? PreferredAppMode.ForceLight : PreferredAppMode.ForceDark));
            }
            else
            {
                // Build 17763 (1809): ordinal #135 is BOOL AllowDarkModeForApp(BOOL).
                NativeMethods.AllowDarkModeForApp(!light);
            }

            NativeMethods.RefreshImmersiveColorPolicyState();
            NativeMethods.FlushMenuThemes();
        }
        catch (DllNotFoundException)
        {
            _darkModeApisUnavailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            _darkModeApisUnavailable = true;
        }
    }

    /// <summary>Applies the light/dark theme to a native window's frame, scroll bars and menus.</summary>
    internal static void ApplyNativeWindowTheme(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ApplyNativeMenuTheme();

        var useDarkMode = !IsLightTheme && Capabilities.SupportsDarkModeApis && !_darkModeApisUnavailable;
        try
        {
            if (Capabilities.SupportsDarkModeApis && !_darkModeApisUnavailable)
            {
                NativeMethods.AllowDarkModeForWindow(handle, useDarkMode);
            }
        }
        catch (DllNotFoundException)
        {
            _darkModeApisUnavailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            _darkModeApisUnavailable = true;
        }

        try
        {
            // DWMWA_USE_IMMERSIVE_DARK_MODE is 19 before build 18985 and 20 from it; Core picks the number.
            if (Capabilities.ImmersiveDarkModeAttribute is int attribute)
            {
                var value = useDarkMode ? 1 : 0;
                _ = NativeMethods.DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
            }

            _ = NativeMethods.SetWindowTheme(handle, useDarkMode ? "DarkMode_Explorer" : "Explorer", null);
        }
        catch (DllNotFoundException)
        {
            // dwmapi/uxtheme missing (Server Core and similar): the window keeps the classic look.
            _darkModeApisUnavailable = true;
        }
        catch (EntryPointNotFoundException)
        {
            _darkModeApisUnavailable = true;
        }
    }

    internal static class Colors
    {
        /// <summary>The DWM colorization colour (cached until <see cref="ThemeHelper.Invalidate"/>).</summary>
        internal static Color GetSystemAccentColor()
        {
            lock (Sync)
            {
                return _accentColor ??= ReadAccentColor();
            }
        }

        internal static Color GetFlyoutBackgroundColor()
        {
            if (SystemInfoHelper.IsWindows11)
            {
                return IsLightTheme ? Color.FromArgb(243, 243, 243) : Color.FromArgb(43, 43, 43);
            }

            return IsLightTheme ? Color.FromArgb(221, 221, 221) : Color.FromArgb(34, 34, 34);
        }

        internal static Color GetFlyoutButtonBackgroundColor()
        {
            if (SystemInfoHelper.IsWindows11)
            {
                return IsLightTheme ? Color.FromArgb(251, 251, 251) : Color.FromArgb(53, 53, 53);
            }

            return IsLightTheme ? Color.FromArgb(251, 251, 251) : Color.FromArgb(45, 45, 45);
        }

        internal static Color GetFlyoutButtonHoverBackgroundColor()
        {
            if (SystemInfoHelper.IsWindows11)
            {
                return IsLightTheme ? Color.FromArgb(245, 245, 245) : Color.FromArgb(61, 61, 61);
            }

            return IsLightTheme ? Color.FromArgb(245, 245, 245) : Color.FromArgb(50, 50, 50);
        }

        internal static Color GetFlyoutButtonPressedBackgroundColor()
        {
            if (SystemInfoHelper.IsWindows11)
            {
                return IsLightTheme ? Color.FromArgb(236, 236, 236) : Color.FromArgb(68, 68, 68);
            }

            return IsLightTheme ? Color.FromArgb(236, 236, 236) : Color.FromArgb(58, 58, 58);
        }

        internal static Color GetFlyoutBorderColor()
        {
            if (SystemInfoHelper.IsWindows11)
            {
                return IsLightTheme ? Color.FromArgb(193, 193, 193) : Color.FromArgb(82, 82, 82);
            }

            return IsLightTheme ? Color.FromArgb(193, 193, 193) : Color.FromArgb(85, 85, 85);
        }

        internal static Color GetFlyoutButtonForegroundColor()
        {
            return IsLightTheme ? Color.FromArgb(32, 32, 32) : Color.FromArgb(245, 245, 245);
        }

        internal static Color GetFlyoutButtonArrowColor()
        {
            return IsLightTheme ? Color.FromArgb(99, 99, 99) : Color.FromArgb(190, 190, 190);
        }

        /// <summary>Shared instance: callers must NOT dispose it.</summary>
        internal static Font GetFlyoutButtonFont() => FlyoutButtonFont.Value;

        /// <summary>Shared instance: callers must NOT dispose it.</summary>
        internal static Font GetFlyoutNumberFont() => FlyoutNumberFont.Value;

        internal static Color GetCountdownBackgroundColor()
        {
            var accentColor = GetSystemAccentColor();
            return IsLightTheme
                ? BlendColors(accentColor, Color.White, 0.18)
                : BlendColors(accentColor, Color.Black, 0.18);
        }

        internal static Color GetCountdownForegroundColor()
        {
            // Black or white by actual WCAG contrast against the accent-derived badge (Core decides; a fixed
            // luminance threshold gave white text below 3:1 on mid-tone accents such as orange).
            return ColorContrast.ReadableText(GetCountdownBackgroundColor(), Color.FromArgb(20, 20, 20), Color.FromArgb(255, 255, 255));
        }

        /// <summary>
        /// Shared instance: callers must NOT dispose it. <paramref name="compact"/> is kept for source compatibility;
        /// the original returned the same font for both values.
        /// </summary>
        internal static Font GetCountdownFont(bool compact) => CountdownFont.Value;

        internal static Color GetBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(255, 255, 255) : Color.FromArgb(45, 45, 45);
        }

        internal static Color GetSettingsBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
        }

        internal static Color GetForegroundColor()
        {
            return IsLightTheme ? Color.FromArgb(0, 0, 0) : Color.FromArgb(255, 255, 255);
        }

        internal static Color GetPanelBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(240, 240, 240) : Color.FromArgb(45, 45, 45);
        }

        internal static Color GetSettingsPanelBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(249, 249, 249) : Color.FromArgb(45, 45, 45);
        }

        internal static Color GetSettingsSelectedTabBackgroundColor()
        {
            return IsLightTheme ? GetSettingsPanelBackgroundColor() : Color.FromArgb(45, 45, 45);
        }

        internal static Color GetSettingsSelectedTabForegroundColor()
        {
            return IsLightTheme ? Color.FromArgb(0, 92, 197) : Color.FromArgb(240, 240, 240);
        }

        internal static Color GetSettingsUnselectedTabForegroundColor()
        {
            return IsLightTheme ? Color.FromArgb(60, 60, 60) : Color.FromArgb(184, 184, 184);
        }

        internal static Color GetSettingsFooterButtonBorderColor()
        {
            return IsLightTheme ? Color.FromArgb(182, 182, 182) : Color.FromArgb(89, 89, 89);
        }

        internal static Color GetSettingsFooterButtonBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(248, 248, 248) : Color.FromArgb(64, 64, 64);
        }

        internal static Color GetSettingsFooterButtonHoverBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(242, 242, 242) : Color.FromArgb(70, 70, 70);
        }

        internal static Color GetSettingsFooterButtonPressedBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(236, 236, 236) : Color.FromArgb(52, 52, 52);
        }

        internal static Color GetSettingsDisabledButtonBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(244, 244, 244) : Color.FromArgb(54, 54, 54);
        }

        internal static Color GetSettingsDisabledButtonBorderColor()
        {
            return IsLightTheme ? Color.FromArgb(182, 182, 182) : Color.FromArgb(72, 72, 72);
        }

        internal static Color GetSettingsDisabledButtonForegroundColor()
        {
            return Color.FromArgb(75, 75, 75);
        }

        internal static Color GetControlBackgroundColor()
        {
            return IsLightTheme ? Color.FromArgb(255, 255, 255) : Color.FromArgb(55, 55, 55);
        }

        internal static Color GetAccentColor()
        {
            return Color.FromArgb(238, 121, 59);
        }

        internal static Color GetLinkColor()
        {
            return IsLightTheme ? Color.FromArgb(0, 120, 215) : Color.FromArgb(238, 121, 59);
        }

        private static Color BlendColors(Color baseColor, Color overlayColor, double overlayAmount)
        {
            var clampedOverlayAmount = Math.Clamp(overlayAmount, 0d, 1d);
            var baseAmount = 1d - clampedOverlayAmount;
            return Color.FromArgb(
                (int)Math.Round((baseColor.R * baseAmount) + (overlayColor.R * clampedOverlayAmount)),
                (int)Math.Round((baseColor.G * baseAmount) + (overlayColor.G * clampedOverlayAmount)),
                (int)Math.Round((baseColor.B * baseAmount) + (overlayColor.B * clampedOverlayAmount)));
        }

    }

    private static bool ReadIsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            if (key?.GetValue("AppsUseLightTheme") is int appValue)
            {
                return appValue == 1;
            }

            if (key?.GetValue("SystemUsesLightTheme") is int systemValue)
            {
                return systemValue == 1;
            }
        }
        catch (SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }

        return false;
    }

    private static Color ReadAccentColor()
    {
        try
        {
            if (NativeMethods.DwmGetColorizationColor(out var colorValue, out _) == 0)
            {
                var r = (byte)((colorValue >> 16) & 0xFF);
                var g = (byte)((colorValue >> 8) & 0xFF);
                var b = (byte)(colorValue & 0xFF);
                return Color.FromArgb(r, g, b);
            }
        }
        catch (DllNotFoundException)
        {
            return DefaultAccentColor;
        }
        catch (EntryPointNotFoundException)
        {
            return DefaultAccentColor;
        }

        return DefaultAccentColor;
    }
}
