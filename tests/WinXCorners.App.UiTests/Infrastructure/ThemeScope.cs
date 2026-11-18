using Microsoft.Win32;

namespace WinXCorners.App.UiTests.Infrastructure;

public enum Theme
{
    Light,
    Dark
}

/// <summary>
/// Switches the current user's app theme (HKCU AppsUseLightTheme) for the duration of a test and restores it.
/// Windows created inside the scope pick up the theme through <see cref="ThemeHelper"/>.
/// </summary>
internal sealed class ThemeScope : IDisposable
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string ValueName = "AppsUseLightTheme";
    private readonly object? _previous;

    private ThemeScope(Theme theme)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        _previous = key.GetValue(ValueName);
        key.SetValue(ValueName, theme == Theme.Light ? 1 : 0, RegistryValueKind.DWord);
        ThemeHelper.Invalidate();
        Assert.Equal(theme == Theme.Light, ThemeHelper.IsLightTheme);
    }

    internal static ThemeScope Use(Theme theme) => new(theme);

    public void Dispose()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true))
        {
            if (_previous is null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(ValueName, _previous, RegistryValueKind.DWord);
            }
        }

        ThemeHelper.Invalidate();
    }
}
