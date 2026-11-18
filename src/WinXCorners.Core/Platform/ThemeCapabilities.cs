namespace WinXCorners.Core.Platform;

/// <summary>
/// Which undocumented dark-mode entry points are safe to call on a given Windows build (review item S5).
/// The uxtheme ordinals #104, #133, #135 and #136 only exist from build 17763 (1809), and #135 changed meaning in
/// 18362 (1903): before it is <c>AllowDarkModeForApp(BOOL)</c>, from it <c>SetPreferredAppMode(PreferredAppMode)</c>.
/// Calling a missing ordinal may hit an unrelated export, so the app must not call them at all when unsupported.
/// </summary>
/// <param name="SupportsDarkModeApis">The uxtheme ordinals may be called.</param>
/// <param name="UsesSetPreferredAppMode">Ordinal #135 takes the PreferredAppMode enum (otherwise a BOOL).</param>
/// <param name="ImmersiveDarkModeAttribute">DWMWA_USE_IMMERSIVE_DARK_MODE for this build, or null when it does not exist.</param>
public sealed record ThemeCapabilities(bool SupportsDarkModeApis, bool UsesSetPreferredAppMode, int? ImmersiveDarkModeAttribute)
{
    public const int FirstDarkModeBuild = 17763;
    public const int FirstSetPreferredAppModeBuild = 18362;
    public const int FirstAttribute20Build = 18985;

    public static ThemeCapabilities ForWindowsBuild(int build)
    {
        if (build < FirstDarkModeBuild)
        {
            return new ThemeCapabilities(false, false, null);
        }

        return new ThemeCapabilities(
            SupportsDarkModeApis: true,
            UsesSetPreferredAppMode: build >= FirstSetPreferredAppModeBuild,
            ImmersiveDarkModeAttribute: build >= FirstAttribute20Build ? 20 : 19);
    }
}
