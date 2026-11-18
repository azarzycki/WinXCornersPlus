using Microsoft.Win32;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Platform;

namespace WinXCorners.App.Tests.Platform;

/// <summary>The HKCU Run entry, using a throw-away value name (the real "WinXCornersPlus" value is never touched).</summary>
public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _valueName = "WinXCornersPlus.Test." + Guid.NewGuid().ToString("N");
    private readonly string _exePath = @"C:\Program Files\WinXCornersPlus Test\WinXCornersPlus.exe";
    private readonly BoundedLog _log = new();

    public void Dispose()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath, writable: true);
        key?.DeleteValue(_valueName, throwOnMissingValue: false);
    }

    private string? ReadRaw()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath);
        return key?.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    private void WriteRaw(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RunKeyPath);
        key.SetValue(_valueName, value, RegistryValueKind.String);
    }

    private static string[] RunValueNames()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath);
        return key?.GetValueNames().OrderBy(static n => n, StringComparer.Ordinal).ToArray() ?? [];
    }

    [Fact]
    public void Constructing_and_reading_writes_nothing()
    {
        var before = RunValueNames();

        var registration = new StartupRegistration(_log, _valueName, _exePath);
        Assert.False(registration.IsEnabled());
        Assert.Null(registration.GetStaleEntryPath());

        Assert.Equal(before, RunValueNames());
        Assert.Null(ReadRaw());
    }

    [Fact]
    public void Enable_writes_exactly_the_quoted_executable_path_and_is_then_enabled()
    {
        var registration = new StartupRegistration(_log, _valueName, _exePath);

        Assert.True(registration.TrySetEnabled(true, out var error), error);

        Assert.Equal("\"" + _exePath + "\"", ReadRaw());
        Assert.Equal(StartupCommandLine.Build(_exePath), ReadRaw());
        Assert.True(registration.IsEnabled());
        Assert.Null(registration.GetStaleEntryPath());
        using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath)!;
        Assert.Equal(RegistryValueKind.String, key.GetValueKind(_valueName));
    }

    [Fact]
    public void Enabled_is_true_only_for_this_executable()
    {
        Assert.True(new StartupRegistration(_log, _valueName, _exePath).TrySetEnabled(true, out _));

        var other = new StartupRegistration(_log, _valueName, @"C:\Elsewhere\WinXCornersPlus.exe");

        Assert.False(other.IsEnabled());
    }

    [Fact]
    public void A_value_pointing_elsewhere_is_reported_as_stale_and_not_rewritten()
    {
        const string stale = "\"C:\\Old Folder\\WinXCornersPlus.exe\"";
        WriteRaw(stale);
        var registration = new StartupRegistration(_log, _valueName, _exePath);

        Assert.False(registration.IsEnabled());
        Assert.Equal(@"C:\Old Folder\WinXCornersPlus.exe", registration.GetStaleEntryPath());
        Assert.Equal(stale, ReadRaw());
    }

    [Fact]
    public void An_unquoted_value_with_spaces_is_not_treated_as_this_executable()
    {
        WriteRaw(_exePath); // Unquoted: CreateProcess could run "C:\Program.exe" instead.
        var registration = new StartupRegistration(_log, _valueName, _exePath);

        Assert.False(registration.IsEnabled());
        Assert.NotNull(registration.GetStaleEntryPath());
    }

    [Fact]
    public void Disable_removes_the_value_including_a_stale_one()
    {
        WriteRaw("\"C:\\Old Folder\\WinXCornersPlus.exe\"");
        var registration = new StartupRegistration(_log, _valueName, _exePath);

        Assert.True(registration.TrySetEnabled(false, out var error), error);

        Assert.Null(ReadRaw());
        Assert.False(registration.IsEnabled());
        Assert.True(registration.TrySetEnabled(false, out _), "Disabling twice is fine.");
    }

    [Fact]
    public void Changes_are_logged()
    {
        var registration = new StartupRegistration(_log, _valueName, _exePath);
        registration.TrySetEnabled(true, out _);
        registration.TrySetEnabled(false, out _);

        var log = _log.Snapshot();
        Assert.Contains(log, line => line.Contains("Run at startup enabled", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("Run at startup disabled", StringComparison.Ordinal));
    }
}
