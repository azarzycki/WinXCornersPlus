using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// The only place that starts programs, documents and links for corner actions and the UI. Core's
/// <see cref="LaunchPlanner"/> decides file, arguments, working directory and token; this class just performs it.
/// </summary>
internal sealed class ProcessLauncher : IProcessLauncher
{
    private readonly BoundedLog _log;
    private readonly SystemDirectories _directories;
    private readonly bool _isElevated;

    internal ProcessLauncher(BoundedLog log, SystemDirectories directories, bool isElevated)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _isElevated = isElevated;
    }

    /// <summary>Plans and performs <paramref name="request"/>. Logs the outcome; never throws for a launch failure.</summary>
    public bool TryLaunch(LaunchRequest request, out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!LaunchPlanner.TryPlan(request, _isElevated, _directories, out var plan, out var planError))
        {
            error = planError;
            _log.Add($"Launch refused: {planError} Target: {DisplayText.ForDisplay(request.Target)}");
            return false;
        }

        var description = LaunchPlanner.Describe(plan);
        _log.Add($"Launching {description}");

        var succeeded = plan.Integrity == LaunchIntegrity.DeElevated
            ? DeElevatedLauncher.TryShellExecute(plan.FileName, plan.Arguments, plan.WorkingDirectory, plan.Hidden, out error)
            : TryStartSameRights(plan, out error);

        _log.Add(succeeded ? $"Launched {description}" : $"Launch failed: {error} ({description})");
        return succeeded;
    }

    private static bool TryStartSameRights(LaunchPlan plan, out string? error)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = plan.FileName,
            Arguments = plan.Arguments,
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = true,
            WindowStyle = plan.Hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
        };

        try
        {
            // ShellExecute may hand the request to an existing process (e.g. a browser) and return no Process.
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
        catch (FileNotFoundException ex)
        {
            error = ex.Message;
        }
        catch (COMException ex)
        {
            error = ex.Message;
        }
        catch (PlatformNotSupportedException ex)
        {
            error = ex.Message;
        }

        return false;
    }
}
