using System.Diagnostics.CodeAnalysis;
using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Security;

/// <summary>With which token a planned launch starts.</summary>
public enum LaunchIntegrity
{
    /// <summary>Same rights as the app, which is only ever the case when the app itself is not elevated.</summary>
    SameAsApp,
    /// <summary>
    /// Started with the desktop shell's (non-elevated) token. If that fails the launch fails: there is no elevated
    /// fallback (fail closed).
    /// </summary>
    DeElevated
}

public enum LaunchTargetKind
{
    /// <summary>A Windows component started from an absolute system path or a shell URI such as <c>ms-settings:</c>.</summary>
    BuiltIn,
    /// <summary>A program, document or URL the user typed into a custom command slot.</summary>
    CustomCommand,
    /// <summary>A web page (About/Updates), opened in the default browser. Always <c>https://</c>.</summary>
    Link
}

public sealed record LaunchRequest(string Target, string Arguments, bool Hidden, LaunchTargetKind Kind);

/// <summary>Everything the app needs for a ShellExecute launch. The app must not override any of it.</summary>
public sealed record LaunchPlan(string FileName, string Arguments, string WorkingDirectory, bool Hidden, LaunchIntegrity Integrity);

/// <summary>
/// Turns a launch request into a plan (review items S1-S3). Every launch from an elevated app is de-elevated; there
/// is deliberately no way to ask for an elevated launch, because a program that needs admin raises its own UAC
/// prompt. The working directory is never the app folder, so ShellExecute's current-directory search cannot pick
/// up a planted file there.
/// </summary>
public static class LaunchPlanner
{
    /// <summary>Shell URI schemes a <see cref="LaunchTargetKind.BuiltIn"/> target may use instead of an absolute path.</summary>
    public static IReadOnlyList<string> BuiltInUriSchemes { get; } = ["ms-settings:"];

    public static bool TryPlan(LaunchRequest request, bool appIsElevated, SystemDirectories directories,
        [NotNullWhen(true)] out LaunchPlan? plan, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(directories);
        plan = null;

        var target = (request.Target ?? string.Empty).Trim();
        var arguments = (request.Arguments ?? string.Empty).Trim();

        error = ValidateText(ref target, arguments) ?? ValidateKind(request.Kind, target, directories);
        if (error is not null)
        {
            return false;
        }

        var workingDirectory = request.Kind == LaunchTargetKind.BuiltIn ? directories.System32 : directories.UserProfile;
        if (!WindowsPath.IsFullyQualified(workingDirectory))
        {
            error = "The working directory supplied for this launch is not an absolute path.";
            return false;
        }

        plan = new LaunchPlan(
            FileName: target,
            Arguments: arguments,
            WorkingDirectory: WindowsPath.Normalize(workingDirectory),
            Hidden: request.Hidden && request.Kind == LaunchTargetKind.CustomCommand,
            Integrity: appIsElevated ? LaunchIntegrity.DeElevated : LaunchIntegrity.SameAsApp);
        return true;
    }

    /// <summary>One log line: target and arguments, with any control characters escaped, plus how it starts.</summary>
    public static string Describe(LaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var text = DisplayText.Escape(plan.FileName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(plan.Arguments))
        {
            text += " " + DisplayText.Escape(plan.Arguments);
        }

        var flags = new List<string>();
        if (plan.Integrity == LaunchIntegrity.DeElevated)
        {
            flags.Add("without administrator rights");
        }

        if (plan.Hidden)
        {
            flags.Add("hidden");
        }

        return flags.Count == 0 ? text : $"{text} ({string.Join(", ", flags)})";
    }

    internal static bool IsHttpsLink(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               url.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
               Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               uri.Host.Length > 0;
    }

    private static string? ValidateText(ref string target, string arguments)
    {
        // Users often paste a quoted path; the quotes are not part of the file name.
        if (target.Length >= 2 && target[0] == '"' && target[^1] == '"')
        {
            target = target[1..^1].Trim();
        }

        if (target.Length == 0)
        {
            return "Nothing to launch: the target is empty.";
        }

        if (target.Any(char.IsControl) || arguments.Any(char.IsControl))
        {
            return "The target or its arguments contain control characters, so the launch was refused.";
        }

        // A quote cannot appear in a file name; inside the target it would only serve to smuggle arguments.
        return target.Contains('"') ? "The target contains a quotation mark, so the launch was refused." : null;
    }

    private static string? ValidateKind(LaunchTargetKind kind, string target, SystemDirectories directories)
    {
        switch (kind)
        {
            case LaunchTargetKind.CustomCommand:
                return null;
            case LaunchTargetKind.Link:
                return IsHttpsLink(target) ? null : "Links must be absolute https:// addresses.";
            case LaunchTargetKind.BuiltIn:
                if (BuiltInUriSchemes.Any(scheme => target.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
                {
                    return null;
                }

                // Built-ins must never be resolved through a search path (review S3).
                return WindowsPath.IsUnder(target, directories.Windows) || WindowsPath.IsUnder(target, directories.System32)
                    ? null
                    : "Built-in targets must be absolute paths inside the Windows folder.";
            default:
                return "Unknown launch kind.";
        }
    }
}

/// <summary>Launch requests for the built-in actions and links, all with absolute targets.</summary>
public static class SystemTargets
{
    public static LaunchRequest FileExplorer(SystemDirectories d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new LaunchRequest(WindowsPath.Combine(d.Windows, "explorer.exe"), string.Empty, false, LaunchTargetKind.BuiltIn);
    }

    public static LaunchRequest TaskManager(SystemDirectories d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new LaunchRequest(WindowsPath.Combine(d.System32, "taskmgr.exe"), string.Empty, false, LaunchTargetKind.BuiltIn);
    }

    public static LaunchRequest SystemSettings() => new("ms-settings:", string.Empty, false, LaunchTargetKind.BuiltIn);

    /// <exception cref="ArgumentException"><paramref name="url"/> is not an absolute https:// address.</exception>
    public static LaunchRequest Link(string url)
    {
        if (!LaunchPlanner.IsHttpsLink(url))
        {
            throw new ArgumentException("Only absolute https:// links can be opened.", nameof(url));
        }

        return new LaunchRequest(url.Trim(), string.Empty, false, LaunchTargetKind.Link);
    }
}
