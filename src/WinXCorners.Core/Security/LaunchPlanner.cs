using System.Diagnostics.CodeAnalysis;
using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Security;

/// <summary>With which token a planned launch starts.</summary>
public enum LaunchIntegrity
{
    /// <summary>Same rights as the app, which is not elevated.</summary>
    SameAsApp,
    /// <summary>
    /// Started with the desktop shell's (non-elevated) token. If that fails, or the shell itself turns out to run as
    /// administrator, the launch fails: there is no elevated fallback (fail closed).
    /// </summary>
    DeElevated,
    /// <summary>
    /// Started directly with the app's elevated token. Only for <see cref="ElevationMode.ElevatedWithoutStandardRights"/>,
    /// where everything on the desktop (the shell included) already runs as administrator, so no other way exists and
    /// this way gives nothing away; the user is told so by <see cref="SecurityPosture"/>.
    /// </summary>
    Administrator
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
/// Turns a launch request into a plan (review items S1-S3, V2 D4). Every launch from an elevated app that has a
/// normal-rights shell is de-elevated; there is deliberately no way to ask for an elevated launch, because a program
/// that needs admin raises its own UAC prompt. Only where no normal-rights context exists at all
/// (<see cref="ElevationMode.ElevatedWithoutStandardRights"/>) is a program started with the app's own token. The working directory is never the app folder, so ShellExecute's current-directory search cannot pick
/// up a planted file there.
/// </summary>
public static class LaunchPlanner
{
    /// <summary>Shell URI schemes a <see cref="LaunchTargetKind.BuiltIn"/> target may use instead of an absolute path.</summary>
    public static IReadOnlyList<string> BuiltInUriSchemes { get; } = ["ms-settings:"];

    public static bool TryPlan(LaunchRequest request, ElevationMode elevation, SystemDirectories directories,
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

        // Custom commands and links start in the user-profile folder, as the Windows Run dialog does (by design,
        // so relative paths in a command behave the way users expect from Run); built-ins start in System32.
        var workingDirectory = request.Kind == LaunchTargetKind.BuiltIn ? directories.System32 : directories.UserProfile;
        if (!WindowsPath.IsFullyQualified(workingDirectory))
        {
            error = "The working directory supplied for this launch is not an absolute path.";
            return false;
        }

        plan = new LaunchPlan(
            // A built-in path is launched in the normalized form that was validated, never as given: Windows
            // does not resolve ".." the same way for every spelling, so what was checked must be what runs.
            FileName: request.Kind == LaunchTargetKind.BuiltIn && !IsBuiltInUri(target) ? WindowsPath.Normalize(target) : target,
            Arguments: arguments,
            WorkingDirectory: WindowsPath.Normalize(workingDirectory),
            Hidden: request.Hidden && request.Kind == LaunchTargetKind.CustomCommand,
            Integrity: elevation switch
            {
                ElevationMode.NotElevated => LaunchIntegrity.SameAsApp,
                ElevationMode.ElevatedWithoutStandardRights => LaunchIntegrity.Administrator,
                // ElevatedWithStandardShell, and any value this version does not know: the fail-closed path.
                _ => LaunchIntegrity.DeElevated
            });
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
        else if (plan.Integrity == LaunchIntegrity.Administrator)
        {
            flags.Add("with administrator rights: this account has no other kind");
        }

        if (plan.Hidden)
        {
            flags.Add("hidden");
        }

        return flags.Count == 0 ? text : $"{text} ({string.Join(", ", flags)})";
    }

    /// <summary>
    /// An absolute https:// address with a host and no user info. User info (<c>https://github.com@evil.example/</c>)
    /// lets the visible start of a link name a trusted site while another one loads, and a backslash is read as
    /// <c>/</c> (<c>https://evil.example\@github.com/</c>), so both are refused.
    /// </summary>
    internal static bool IsHttpsLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        var text = url.Trim();
        return text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains('\\') &&
               Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               uri.UserInfo.Length == 0 &&
               uri.Host.Length > 0;
    }

    private static bool IsBuiltInUri(string target) =>
        BuiltInUriSchemes.Any(scheme => target.StartsWith(scheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <c>\\?\</c>, <c>\\.\</c> (with either slash) and <c>\??\</c> paths bypass Win32 path normalization, so the
    /// lexical check could approve one spelling while Windows opens another.
    /// </summary>
    private static bool IsDevicePath(string target)
    {
        static bool IsSeparator(char c) => c is '\\' or '/';
        return target.Length >= 4 &&
               ((IsSeparator(target[0]) && IsSeparator(target[1]) && (target[2] is '?' or '.') && IsSeparator(target[3])) ||
                target.StartsWith(@"\??\", StringComparison.Ordinal));
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
                if (IsBuiltInUri(target))
                {
                    return null;
                }

                if (IsDevicePath(target))
                {
                    return "Built-in targets must be plain absolute paths, not device or extended-length paths.";
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
