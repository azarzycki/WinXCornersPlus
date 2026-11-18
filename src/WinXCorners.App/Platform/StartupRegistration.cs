using System.Security;
using Microsoft.Win32;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Reads and changes the per-user autostart entry
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, value <c>WinXCornersPlus</c>).
/// </summary>
/// <remarks>
/// Unlike v1.4, nothing is written at startup: the value changes only when the user changes the setting, so a
/// stale entry (the app was moved) is reported through <see cref="GetStaleEntryPath"/> instead of silently
/// rewritten. The value is always written quoted (Core's <see cref="StartupCommandLine"/>) so a path with spaces
/// cannot be split into a shorter, planted program name.
/// </remarks>
internal sealed class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string DefaultValueName = "WinXCornersPlus";

    private readonly BoundedLog _log;
    private readonly string _valueName;
    private readonly string _executablePath;

    /// <param name="log">Where changes and failures are written.</param>
    /// <param name="valueName">Run value name; tests pass a throw-away name.</param>
    /// <param name="executablePath">The program the entry should start; defaults to this process's executable.</param>
    internal StartupRegistration(BoundedLog log, string valueName = DefaultValueName, string? executablePath = null)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        _valueName = valueName;
        _executablePath = executablePath ?? Environment.ProcessPath ?? string.Empty;
    }

    /// <summary>True only when the Run value starts exactly this executable.</summary>
    internal bool IsEnabled()
    {
        return StartupCommandLine.PointsTo(ReadValue(), _executablePath);
    }

    /// <summary>The program a Run value of ours starts when it is NOT this executable; null otherwise.</summary>
    /// <summary>What the Run value says about this executable (an unquoted path with spaces is never "this one").</summary>
    internal RunEntryState GetEntryState() => StartupCommandLine.Classify(ReadValue(), _executablePath);

    internal string? GetStaleEntryPath()
    {
        var value = ReadValue();
        if (string.IsNullOrWhiteSpace(value) || StartupCommandLine.PointsTo(value, _executablePath))
        {
            return null;
        }

        return StartupCommandLine.TryGetExecutablePath(value, out var path) ? path : value;
    }

    internal bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                error = "The startup key could not be opened.";
                _log.Add($"Run at startup not changed: {error}");
                return false;
            }

            if (enabled)
            {
                var command = StartupCommandLine.Build(_executablePath);
                key.SetValue(_valueName, command, RegistryValueKind.String);
                _log.Add($"Run at startup enabled: {DisplayText.ForDisplay(command)}");
            }
            else
            {
                // Removes a stale entry of ours too: the user asked for no autostart.
                key.DeleteValue(_valueName, throwOnMissingValue: false);
                _log.Add("Run at startup disabled.");
            }

            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = $"This program's path cannot be used for autostart: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
        }
        catch (SecurityException ex)
        {
            error = ex.Message;
        }
        catch (IOException ex)
        {
            error = ex.Message;
        }

        _log.Add($"Run at startup not changed: {error}");
        return false;
    }

    private string? ReadValue()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(_valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
        catch (SecurityException ex)
        {
            _log.Add($"Run at startup could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Add($"Run at startup could not be read: {ex.Message}");
        }
        catch (IOException ex)
        {
            _log.Add($"Run at startup could not be read: {ex.Message}");
        }

        return null;
    }
}
