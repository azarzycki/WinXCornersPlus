using System.Drawing;
using System.Reflection;
using Microsoft.Win32;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Platform;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// ThemeHelper on the real OS build: colours and fonts in light and dark mode (by flipping HKCU AppsUseLightTheme and
/// restoring it), the uxtheme ordinal guards, embedded icons and the taskbar query.
/// </summary>
public sealed class ThemeHelperTests : IDisposable
{
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    private readonly ITestOutputHelper _output;
    private readonly object? _originalValue;

    public ThemeHelperTests(ITestOutputHelper output)
    {
        _output = output;
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
        _originalValue = key?.GetValue(AppsUseLightTheme);
    }

    public void Dispose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath);
        if (_originalValue is null)
        {
            key.DeleteValue(AppsUseLightTheme, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(AppsUseLightTheme, _originalValue, RegistryValueKind.DWord);
        }

        ThemeHelper.Invalidate();
    }

    private static void SetLightTheme(bool light)
    {
        using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath);
        key.SetValue(AppsUseLightTheme, light ? 1 : 0, RegistryValueKind.DWord);
    }

    private static IEnumerable<MethodInfo> ColorGetters() =>
        typeof(ThemeHelper.Colors).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(static m => m.ReturnType == typeof(Color) && m.GetParameters().Length == 0 && m.Name.StartsWith("Get", StringComparison.Ordinal));

    [Fact]
    public void The_running_build_and_its_theme_capabilities_are_logged()
    {
        var build = Environment.OSVersion.Version.Build;
        var capabilities = ThemeCapabilities.ForWindowsBuild(build);
        _output.WriteLine($"{TestEnvironment.Describe()}; Windows 11: {SystemInfoHelper.IsWindows11}; {capabilities}");
        Assert.True(build >= 17763, "The app targets Windows 10 1809 or later.");
        Assert.True(capabilities.SupportsDarkModeApis);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_colour_works_and_follows_the_theme(bool light)
    {
        SetLightTheme(light);
        ThemeHelper.Invalidate();

        Assert.Equal(light, ThemeHelper.IsLightTheme);
        var getters = ColorGetters().ToList();
        Assert.True(getters.Count >= 25, $"Only {getters.Count} colour getters were found.");
        foreach (var getter in getters)
        {
            var color = (Color)getter.Invoke(null, null)!;
            Assert.True(color.A == 255, $"{getter.Name} is not opaque.");
        }

        Assert.Equal(light ? Color.FromArgb(255, 255, 255) : Color.FromArgb(45, 45, 45), ThemeHelper.Colors.GetBackgroundColor());
        Assert.Equal(light ? Color.FromArgb(0, 0, 0) : Color.FromArgb(255, 255, 255), ThemeHelper.Colors.GetForegroundColor());

        // The countdown text must stay readable on its accent-based background.
        var background = ThemeHelper.Colors.GetCountdownBackgroundColor();
        var foreground = ThemeHelper.Colors.GetCountdownForegroundColor();
        var contrast = ContrastRatio(background, foreground);
        Assert.True(contrast >= 3, $"Countdown text {foreground} on {background} has contrast {contrast:0.0}:1.");
    }

    /// <summary>WCAG 2 contrast ratio.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(byte c)
        {
            var v = c / 255d;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color c) => (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }

    [Fact]
    public void The_theme_is_cached_until_Invalidate()
    {
        SetLightTheme(true);
        ThemeHelper.Invalidate();
        Assert.True(ThemeHelper.IsLightTheme);

        SetLightTheme(false);
        Assert.True(ThemeHelper.IsLightTheme); // Still cached (review B8: no registry read per colour).

        ThemeHelper.Invalidate();
        Assert.False(ThemeHelper.IsLightTheme);
    }

    [Fact]
    public void A_missing_theme_value_falls_back_without_throwing()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(PersonalizeKeyPath))
        {
            key.DeleteValue(AppsUseLightTheme, throwOnMissingValue: false);
        }

        ThemeHelper.Invalidate();
        _ = ThemeHelper.IsLightTheme;
        _ = ThemeHelper.Colors.GetSystemAccentColor();
    }

    [Fact]
    public void Fonts_are_created_once_and_shared()
    {
        var button = ThemeHelper.Colors.GetFlyoutButtonFont();
        var number = ThemeHelper.Colors.GetFlyoutNumberFont();
        var countdown = ThemeHelper.Colors.GetCountdownFont(compact: false);

        ThemeHelper.Invalidate();
        Assert.Same(button, ThemeHelper.Colors.GetFlyoutButtonFont());
        Assert.Same(number, ThemeHelper.Colors.GetFlyoutNumberFont());
        Assert.Same(countdown, ThemeHelper.Colors.GetCountdownFont(compact: true));
        _output.WriteLine($"Fonts resolved to: {button.Name} {button.SizeInPoints}pt, {number.Name} {number.SizeInPoints}pt, {countdown.Name} {countdown.SizeInPoints}pt");
        Assert.True(button.SizeInPoints > 0 && number.SizeInPoints > 0 && countdown.SizeInPoints > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_theming_of_a_real_window_and_menus_does_not_throw_on_this_build(bool light)
    {
        SetLightTheme(light);
        ThemeHelper.Invalidate();

        Sta.Run(() =>
        {
            using var window = TestWindow.Create(TestNative.WS_OVERLAPPEDWINDOW, 0, new Rectangle(0, 0, 300, 200));
            ThemeHelper.ApplyNativeMenuTheme();
            ThemeHelper.ApplyNativeWindowTheme(window.Handle);
            ThemeHelper.ApplyNativeWindowTheme(IntPtr.Zero);

            // A native popup menu can be built and destroyed after the menu theme was applied.
            var menu = NativeMethods.CreatePopupMenu();
            Assert.NotEqual(IntPtr.Zero, menu);
            Assert.True(NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, new UIntPtr(1), "Item"));
            Assert.True(NativeMethods.DestroyMenu(menu));
        });
    }

    [Fact]
    public void Every_embedded_icon_loads()
    {
        var resources = typeof(ThemeHelper).Assembly.GetManifestResourceNames();
        _output.WriteLine(string.Join(", ", resources));
        Assert.Equal(7, resources.Count(static r => r.StartsWith("Icons.", StringComparison.Ordinal)));

        var icons = new List<Icon?>
        {
            WindowIconLoader.TryLoadAppIcon(),
            WindowIconLoader.TryLoadAboutIcon(),
            WindowIconLoader.LoadFallbackTrayIcon()
        };
        foreach (var light in new[] { true, false })
        {
            foreach (var enabled in new[] { true, false })
            {
                icons.Add(WindowIconLoader.TryLoadTrayIcon(light, enabled));
            }
        }

        try
        {
            Assert.All(icons, icon =>
            {
                Assert.NotNull(icon);
                Assert.True(icon.Width > 0 && icon.Height > 0);
                Assert.NotEqual(IntPtr.Zero, icon.Handle);
            });
        }
        finally
        {
            icons.ForEach(static icon => icon?.Dispose());
        }
    }

    [Fact]
    public void Each_load_returns_a_new_icon_the_caller_owns()
    {
        using var first = WindowIconLoader.TryLoadAppIcon();
        using var second = WindowIconLoader.TryLoadAppIcon();
        Assert.NotSame(first, second);
        first!.Dispose();
        Assert.True(second!.Width > 0);
    }

    [Fact]
    public void The_primary_taskbar_query_does_not_throw()
    {
        var taskbar = TaskbarInfo.TryGetPrimaryTaskbar();
        _output.WriteLine(taskbar is null ? "No taskbar reported (no shell)." : $"Taskbar: {taskbar.Value.Bounds} at {taskbar.Value.Edge}");
        if (taskbar is { } info)
        {
            Assert.False(info.Bounds.IsEmpty);
        }
    }
}
