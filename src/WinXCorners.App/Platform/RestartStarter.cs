using System.ComponentModel;
using System.Diagnostics;
using WinXCorners.Core.Platform;

namespace WinXCorners.App;

/// <summary>
/// The three ways a restart can start the replacement process (Core's <c>RestartMethod</c>). An interface so tests can
/// assert which one <see cref="AppHost.TryRestart"/> picks, and with which arguments, without starting anything.
/// None of the members throws for an expected failure.
/// </summary>
internal interface IRestartStarter
{
    /// <summary>CreateProcess with this process's own token.</summary>
    bool TryStartDirectly(string executable, IReadOnlyList<string> arguments, out string? error);

    /// <summary>The <c>runas</c> verb. Refuses (fails closed) when this process is already elevated.</summary>
    bool TryStartThroughElevationPrompt(string executable, IReadOnlyList<string> arguments, out string? error);

    /// <summary>ShellExecute inside the normal-rights desktop shell; fails closed when that is not possible.</summary>
    bool TryStartThroughDesktopShell(string executable, IReadOnlyList<string> arguments, out string? error);
}

/// <summary>The production <see cref="IRestartStarter"/>.</summary>
internal sealed class RestartStarter : IRestartStarter
{
    internal static RestartStarter Instance { get; } = new();

    private RestartStarter()
    {
    }

    public bool TryStartDirectly(string executable, IReadOnlyList<string> arguments, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!IsUsablePath(executable, out error))
        {
            return false;
        }

        // CreateProcess with an absolute path and System32 as working directory: nothing is searched for (review S3).
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = CommandLineQuoting.Join(arguments),
            WorkingDirectory = Environment.SystemDirectory,
            UseShellExecute = false
        };

        try
        {
            using var process = Process.Start(startInfo);
            error = null;
            return true;
        }
        catch (Win32Exception ex)
        {
            error = ex.Message;
        }
        catch (InvalidOperationException ex)
        {
            error = ex.Message;
        }
        catch (PlatformNotSupportedException ex)
        {
            error = ex.Message;
        }

        return false;
    }

    public bool TryStartThroughElevationPrompt(string executable, IReadOnlyList<string> arguments, out string? error)
    {
        // ProcessElevation uses this process's own path (the same value) and checks that we are not elevated.
        return ProcessElevation.TryRestartElevated(arguments, out error);
    }

    public bool TryStartThroughDesktopShell(string executable, IReadOnlyList<string> arguments, out string? error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!IsUsablePath(executable, out error))
        {
            return false;
        }

        // Our own token is elevated; only the shell can start us without it. There is no fallback (fail closed).
        return DeElevatedLauncher.TryShellExecute(
            executable, CommandLineQuoting.Join(arguments), Environment.SystemDirectory, hidden: false, out error);
    }

    private static bool IsUsablePath(string executable, out string? error)
    {
        if (string.IsNullOrEmpty(executable) || !Path.IsPathFullyQualified(executable))
        {
            error = "The path of the running program is unknown, so it cannot be restarted.";
            return false;
        }

        error = null;
        return true;
    }
}
