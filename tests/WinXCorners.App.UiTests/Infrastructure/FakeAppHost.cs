using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>
/// An <see cref="IAppHost"/> that does nothing outside the test: settings live in a temp folder, and every side
/// effect (links, Explorer, the Run key, restarts, exit) is recorded instead of performed.
/// </summary>
internal sealed class FakeAppHost : IAppHost, IDisposable
{
    internal const string ProtectedFolder = @"C:\Program Files\WinXCornersPlus";
    internal const string UserWritableFolder = @"C:\Users\Test User\Downloads\WinXCornersPlus-1.5.0";

    private readonly string _directory;

    internal FakeAppHost(ApplicationSettings? initial = null)
    {
        _directory = Path.Combine(Path.GetTempPath(), "wxc-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Store = new SettingsStore(new SettingsPaths(Path.Combine(_directory, SettingsPaths.FileName), null));
        Settings = new SettingsSession(Store, initial ?? new ApplicationSettings());
        Install = ProtectedInstall();
        SettingsPath = Store.Paths.PrimaryPath;
    }

    public BoundedLog Log { get; } = new();

    public SettingsSession Settings { get; }

    public bool IsElevated { get; set; }

    public string VersionText { get; set; } = "9.8.7-uitest";

    public string ExecutablePath { get; set; } = ProtectedFolder + @"\WinXCornersPlus.exe";

    internal SettingsStore Store { get; }

    internal InstallLocationAssessment Install { get; set; }

    /// <summary>The path reported by <see cref="GetSecurityContext"/> (defaults to the temp settings file).</summary>
    internal string SettingsPath { get; set; }

    internal bool StartupEnabled { get; set; }

    /// <summary>When set, <see cref="TrySetStartupEnabled"/> fails with this error and changes nothing.</summary>
    internal string? StartupFailure { get; set; }

    internal bool RestartSucceeds { get; set; }

    internal int SecurityContextRequests { get; private set; }

    internal List<string> OpenedLinks { get; } = [];

    internal int OpenSettingsFolderCalls { get; private set; }

    internal List<bool> StartupChanges { get; } = [];

    internal List<RestartMode> Restarts { get; } = [];

    internal int ExitRequests { get; private set; }

    internal static InstallLocationAssessment ProtectedInstall() =>
        InstallLocationAssessor.Assess(ProtectedFolder, [@"C:\Program Files"], writableByNonAdministrators: false);

    internal static InstallLocationAssessment UserWritableInstall() =>
        InstallLocationAssessor.Assess(UserWritableFolder, [@"C:\Program Files"], writableByNonAdministrators: true);

    public SecurityContext GetSecurityContext()
    {
        SecurityContextRequests++;
        return new SecurityContext(IsElevated, Install, StartupEnabled, SettingsPath);
    }

    public void OpenLink(string httpsUrl) => OpenedLinks.Add(httpsUrl);

    public void OpenSettingsFolder() => OpenSettingsFolderCalls++;

    public bool IsStartupEnabled() => StartupEnabled;

    public bool TrySetStartupEnabled(bool enabled, out string? error)
    {
        StartupChanges.Add(enabled);
        if (StartupFailure is not null)
        {
            error = StartupFailure;
            return false;
        }

        StartupEnabled = enabled;
        error = null;
        return true;
    }

    /// <summary>Core's decision for this host's elevation (elevated means "with a normal-rights desktop") and install kind.</summary>
    public RestartDecision PlanRestart(RestartMode mode) => RestartPolicy.Decide(
        mode switch
        {
            RestartMode.AsAdministrator => RestartRequest.AsAdministrator,
            RestartMode.WithoutAdministrator => RestartRequest.WithoutAdministrator,
            _ => RestartRequest.SameRights
        },
        IsElevated ? ElevationMode.ElevatedWithStandardShell : ElevationMode.NotElevated,
        Install.Kind);

    public bool TryRestart(RestartMode mode, out string? error)
    {
        Restarts.Add(mode);
        error = RestartSucceeds ? null : "the test host does not restart";
        return RestartSucceeds;
    }

    public void RequestExit() => ExitRequests++;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file is still open (e.g. by a virus scanner); the temp folder is cleaned up by the runner.
        }
    }
}

/// <summary>An approvals store in memory (the real one writes HKLM).</summary>
internal sealed class MemoryApprovals : IApprovedSettingsStore
{
    internal string? Stored { get; set; }

    public string? Read() => Stored;

    public bool TryWrite(string approval)
    {
        Stored = approval;
        return true;
    }
}

/// <summary>An <see cref="IRestartStarter"/> that records the way a restart would start the new process.</summary>
internal sealed class RecordingRestartStarter : IRestartStarter
{
    internal List<string> Calls { get; } = [];

    /// <summary>When set, every start fails with this error.</summary>
    internal string? Failure { get; set; }

    public bool TryStartDirectly(string executable, IReadOnlyList<string> arguments, out string? error) => Record("direct", out error);

    public bool TryStartThroughElevationPrompt(string executable, IReadOnlyList<string> arguments, out string? error) => Record("prompt", out error);

    public bool TryStartThroughDesktopShell(string executable, IReadOnlyList<string> arguments, out string? error) => Record("shell", out error);

    private bool Record(string how, out string? error)
    {
        Calls.Add(how);
        error = Failure;
        return Failure is null;
    }
}
