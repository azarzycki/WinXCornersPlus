using System.Globalization;

namespace WinXCorners.Core.Platform;

/// <summary>
/// The app's command-line switches.
/// </summary>
/// <param name="NoElevate">Do not restart as administrator even if "Always run as administrator" is on.</param>
/// <param name="ShowSettings">Open the Settings window after start (also what a second launch asks the running copy).</param>
/// <param name="RestartedFromProcessId">
/// Set when this process was started by an Elevate/Reload restart: the new instance must wait for that process to
/// exit before taking the single-instance mutex, which removes the race in review item B7.
/// </param>
/// <param name="Unrecognized">Arguments that were ignored, kept so the app can log them.</param>
public sealed record CommandLineOptions(bool NoElevate, bool ShowSettings, int? RestartedFromProcessId, IReadOnlyList<string> Unrecognized)
{
    public const string NoElevateSwitch = "--no-elevate";
    public const string ShowSettingsSwitch = "--show-settings";
    public const string RestartedFromSwitch = "--restarted-from";

    /// <summary>
    /// Parses arguments case-insensitively. Never throws: null lists and null entries are tolerated. A
    /// <c>--restarted-from</c> without a positive integer process id leaves <see cref="RestartedFromProcessId"/>
    /// null and is recorded in <see cref="Unrecognized"/>; a following argument that starts with <c>--</c> is not
    /// consumed as its value.
    /// </summary>
    public static CommandLineOptions Parse(IReadOnlyList<string?>? args)
    {
        var noElevate = false;
        var showSettings = false;
        int? restartedFrom = null;
        var unrecognized = new List<string>();
        args ??= [];

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            var token = arg.Trim();
            if (token.Equals(NoElevateSwitch, StringComparison.OrdinalIgnoreCase))
            {
                noElevate = true;
            }
            else if (token.Equals(ShowSettingsSwitch, StringComparison.OrdinalIgnoreCase))
            {
                showSettings = true;
            }
            else if (token.Equals(RestartedFromSwitch, StringComparison.OrdinalIgnoreCase))
            {
                var next = i + 1 < args.Count ? args[i + 1]?.Trim() : null;
                if (next is null || next.StartsWith("--", StringComparison.Ordinal))
                {
                    unrecognized.Add(token);
                    continue;
                }

                i++;
                if (TryParseProcessId(next, out var pid))
                {
                    restartedFrom = pid;
                }
                else
                {
                    unrecognized.Add(token + " " + next);
                }
            }
            else if (token.StartsWith(RestartedFromSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseProcessId(token[(RestartedFromSwitch.Length + 1)..], out var pid))
                {
                    restartedFrom = pid;
                }
                else
                {
                    unrecognized.Add(token);
                }
            }
            else
            {
                unrecognized.Add(token);
            }
        }

        return new CommandLineOptions(noElevate, showSettings, restartedFrom, unrecognized);
    }

    /// <summary>Arguments for the replacement process started by Elevate/Reload.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="currentProcessId"/> is not positive.</exception>
    public static IReadOnlyList<string> BuildRestartArguments(int currentProcessId, bool noElevate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(currentProcessId);
        var arguments = new List<string> { RestartedFromSwitch, currentProcessId.ToString(CultureInfo.InvariantCulture) };
        if (noElevate)
        {
            arguments.Add(NoElevateSwitch);
        }

        return arguments;
    }

    private static bool TryParseProcessId(string text, out int pid)
    {
        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out pid) && pid > 0;
    }
}
