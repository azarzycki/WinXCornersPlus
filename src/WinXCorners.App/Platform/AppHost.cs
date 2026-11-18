using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>
/// The composition root: builds the log, the settings session and every platform service once, and implements
/// <see cref="IAppHost"/> for the windows. Created and used on the UI thread.
/// </summary>
internal sealed class AppHost : IAppHost, IStartupHost, IDisposable
{
    private const int SettingsReadRetries = 3;
    private static readonly TimeSpan SettingsRetryDelay = TimeSpan.FromMilliseconds(150);

    private readonly IApprovedSettingsStore _approvals;
    private readonly SettingsStore _store;
    private readonly Lazy<bool?> _installWritable;
    private bool _reapproveOnChange;
    private bool _disposed;

    private AppHost(
        CommandLineOptions options,
        BoundedLog log,
        SettingsStore store,
        SettingsLoadResult loadResult,
        IApprovedSettingsStore approvals,
        ElevationMode elevation,
        string startupValueName)
    {
        Options = options;
        Log = log;
        _store = store;
        LoadResult = loadResult;
        _approvals = approvals;
        Elevation = elevation;
        IsElevated = elevation.IsElevated();
        Settings = new SettingsSession(store, loadResult.Settings);
        ExecutablePath = Environment.ProcessPath ?? string.Empty;
        VersionText = ReadVersionText();

        Directories = SystemDirectoriesProvider.Get();
        Launcher = new ProcessLauncher(log, Directories, elevation);
        Keys = new KeySender(log);
        Foreground = new ForegroundWindowInspector();
        Startup = new StartupRegistration(log, startupValueName, ExecutablePath);
        Executor = new ActionExecutor(log, Directories, Keys, Launcher, new WindowQuery(), Foreground.IsTaskViewForeground, new Win32SystemActions());

        // Probed once: walking the ACLs is cheap but not free, and the install folder does not change while we run.
        // The executable and the app's own DLLs are checked too: a writable file in a locked folder is just as bad.
        _installWritable = new Lazy<bool?>(() => InstallLocationProbe.IsWritableByNonAdministrators(
            AppContext.BaseDirectory, [ExecutablePath, typeof(AppHost).Assembly.Location, typeof(SettingsStore).Assembly.Location]));
    }

    /// <summary>Raised by <see cref="RequestExit"/> and after a successful <see cref="TryRestart"/>; the main form shuts down.</summary>
    public event Action? ExitRequested;

    public BoundedLog Log { get; }

    public SettingsSession Settings { get; }

    public bool IsElevated { get; }

    /// <summary>The three-state elevation (review V2 D4): decides how programs are launched and how restarts work.</summary>
    public ElevationMode Elevation { get; }

    public string VersionText { get; }

    public string ExecutablePath { get; }

    public CommandLineOptions Options { get; }

    public SettingsLoadResult LoadResult { get; }

    public ActionExecutor Executor { get; }

    public KeySender Keys { get; }

    public ForegroundWindowInspector Foreground { get; }

    public ProcessLauncher Launcher { get; }

    public SystemDirectories Directories { get; }

    public StartupRegistration Startup { get; }

    /// <summary>Production entry point, called once by Program.Main on the UI thread.</summary>
    public static AppHost Create(CommandLineOptions options)
    {
        // Review S3: the process working directory must never be the app folder (or wherever we were started from),
        // because ShellExecute and LoadLibrary search it first.
        Environment.CurrentDirectory = Environment.SystemDirectory;

        var paths = SettingsPaths.ForApp(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppContext.BaseDirectory);
        return Create(options, paths, new ApprovedSettingsStore(), ProcessElevation.CurrentMode, StartupRegistration.DefaultValueName);
    }

    /// <summary>Test entry point with a plain elevated flag: elevated means "with a normal-rights shell".</summary>
    internal static AppHost Create(
        CommandLineOptions options,
        SettingsPaths paths,
        IApprovedSettingsStore approvals,
        bool isElevated,
        string startupValueName)
    {
        return Create(options, paths, approvals,
            isElevated ? ElevationMode.ElevatedWithStandardShell : ElevationMode.NotElevated, startupValueName);
    }

    /// <summary>
    /// Test entry point: explicit settings location, approvals store, elevation flag and Run value name, so a test
    /// never touches the user's real settings, HKLM approval or autostart entry. Does not change the current directory.
    /// </summary>
    internal static AppHost Create(
        CommandLineOptions options,
        SettingsPaths paths,
        IApprovedSettingsStore approvals,
        ElevationMode elevation,
        string startupValueName)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(approvals);

        var log = new BoundedLog();
        // Review V2 D1: nothing an elevated session does before (or after) the approval gate may move, back up,
        // migrate or create a file through a path that programs without administrator rights can redirect.
        var isElevated = elevation.IsElevated();
        var (store, result) = LoadSettings(paths, SettingsStorePolicy.For(isElevated),
            isElevated ? new VerifiedSettingsFile(paths.PrimaryPath) : null, log);
        var host = new AppHost(options, log, store, result, approvals, elevation, startupValueName);
        log.Add($"WinXCornersPlus {host.VersionText} started{elevation switch
        {
            ElevationMode.ElevatedWithStandardShell => " as administrator",
            ElevationMode.ElevatedWithoutStandardRights => " as administrator, on an account where everything runs as administrator",
            _ => string.Empty
        }}.");
        foreach (var argument in options.Unrecognized)
        {
            log.Add($"Ignored command-line argument: {DisplayText.ForDisplay(argument)}");
        }

        return host;
    }

    public SecurityContext GetSecurityContext()
    {
        var install = InstallLocationAssessor.Assess(AppContext.BaseDirectory, Directories.ProtectedRoots, _installWritable.Value);
        return new SecurityContext(IsElevated, install, Startup.IsEnabled(), _store.Paths.PrimaryPath,
            Elevation == ElevationMode.ElevatedWithoutStandardRights);
    }

    public void OpenLink(string httpsUrl)
    {
        LaunchRequest request;
        try
        {
            request = SystemTargets.Link(httpsUrl);
        }
        catch (ArgumentException ex)
        {
            Log.Add($"Link not opened: {ex.Message} ({DisplayText.ForDisplay(httpsUrl)})");
            return;
        }

        Launcher.TryLaunch(request, out _);
    }

    public void OpenSettingsFolder()
    {
        var folder = Path.GetDirectoryName(_store.Paths.PrimaryPath);
        if (string.IsNullOrEmpty(folder))
        {
            Log.Add("The settings folder is unknown.");
            return;
        }

        try
        {
            // Before the first save the folder may not exist, and Explorer would silently open Documents instead.
            // An elevated session creates nothing by path in the user's profile (review V2 D1); its own saves create
            // the folder through verified handles.
            if (!IsElevated)
            {
                Directory.CreateDirectory(folder);
            }
        }
        catch (IOException ex)
        {
            Log.Add($"The settings folder could not be created: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Add($"The settings folder could not be created: {ex.Message}");
        }

        // explorer.exe from the Windows folder (absolute path, a BuiltIn target) with the folder as its argument.
        var request = SystemTargets.FileExplorer(Directories) with { Arguments = CommandLineQuoting.Quote(folder) };
        Launcher.TryLaunch(request, out _);
    }

    public bool IsStartupEnabled() => Startup.IsEnabled();

    public bool TrySetStartupEnabled(bool enabled, out string? error) => Startup.TrySetEnabled(enabled, out error);

    public bool TryRestart(RestartMode mode, out string? error)
    {
        var saved = Settings.Flush();
        if (!saved.Succeeded)
        {
            Log.Add($"Settings could not be saved before the restart: {saved.Error}");
        }

        var arguments = CommandLineOptions.BuildRestartArguments(Environment.ProcessId, noElevate: mode == RestartMode.WithoutAdministrator);
        var request = mode switch
        {
            RestartMode.AsAdministrator => RestartRequest.AsAdministrator,
            RestartMode.WithoutAdministrator => RestartRequest.WithoutAdministrator,
            _ => RestartRequest.SameRights
        };

        // Review V2 D5: an elevated restart starts the binary directly only from a protected folder; otherwise it goes
        // through the UAC prompt, so a replaced executable or DLL never gets the administrator token silently.
        var method = RestartPolicy.Decide(request, Elevation, IsElevated ? GetSecurityContext().Install.Kind : InstallLocationKind.Unknown);
        bool started;
        switch (method)
        {
            case RestartMethod.ThroughElevationPrompt:
                started = ProcessElevation.TryRestartElevated(arguments, out error);
                break;
            case RestartMethod.ThroughDesktopShell:
                // Our own token is elevated; only the shell can start us again without it (fail closed otherwise).
                started = DeElevatedLauncher.TryShellExecute(
                    ExecutablePath, CommandLineQuoting.Join(arguments), Environment.SystemDirectory, hidden: false, out error);
                break;
            case RestartMethod.StartDirectly:
                started = TryStartSameRights(arguments, out error);
                break;
            default:
                started = false;
                error = RestartPolicy.NoStandardRightsText;
                break;
        }

        Log.Add(started ? $"Restarting ({mode}, {method})." : $"Restart ({mode}, {method}) failed: {error}");
        if (started)
        {
            ExitRequested?.Invoke();
        }

        return started;
    }

    public void RequestExit()
    {
        var saved = Settings.Flush();
        if (!saved.Succeeded)
        {
            Log.Add($"Settings could not be saved on exit: {saved.Error}");
        }

        ExitRequested?.Invoke();
    }

    /// <summary>What a normal-rights start does about "Always run as administrator" (Core's AutoElevatePolicy).</summary>
    public AutoElevateAction DecideAutoElevate()
    {
        // Review V2 D3: "an approval exists" is not enough. Only an approval that matches these settings exactly
        // (and so was given with "Always run as administrator" on) lets the start elevate without asking first.
        return AutoElevatePolicy.Decide(
            Settings.Current.AlwaysRunAsAdministrator,
            IsElevated,
            Options.NoElevate,
            SettingsSeal.Check(Settings.Current, _approvals.Read()).Status);
    }

    /// <summary>The settings-seal check for this start (brief design point 2) and what to do about it.</summary>
    public (ElevatedStartAction Action, SealCheck Seal) EvaluateElevatedStart()
    {
        var seal = SettingsSeal.Check(Settings.Current, _approvals.Read());
        return (ElevatedStartPolicy.Decide(IsElevated, seal), seal);
    }

    /// <summary>
    /// Records the current settings as approved for elevated sessions. From then on every settings change made in
    /// this session is re-approved automatically: the change can only come from this process's own (elevated)
    /// windows, which normal-rights programs cannot drive (UIPI), so it is the user's own decision.
    /// </summary>
    public bool ApproveCurrentSettings()
    {
        if (!WriteApproval())
        {
            return false;
        }

        if (!_reapproveOnChange)
        {
            _reapproveOnChange = true;
            Settings.Changed += OnSettingsChanged;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_reapproveOnChange)
        {
            Settings.Changed -= OnSettingsChanged;
        }

        Executor.Dispose();
    }

    private void OnSettingsChanged(ApplicationSettings settings) => WriteApproval();

    private bool WriteApproval()
    {
        var written = _approvals.TryWrite(SettingsSeal.CreateApproval(Settings.Current));
        Log.Add(written
            ? "Settings approved for administrator sessions."
            : "The approval could not be stored (administrator rights are needed to write it).");
        return written;
    }

    private bool TryStartSameRights(IReadOnlyList<string> arguments, out string? error)
    {
        if (string.IsNullOrEmpty(ExecutablePath) || !Path.IsPathFullyQualified(ExecutablePath))
        {
            error = "The path of the running program is unknown, so it cannot be restarted.";
            return false;
        }

        // CreateProcess with an absolute path and System32 as working directory: nothing is searched for (review S3).
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath,
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

    /// <summary>
    /// Loads settings. A ReadFailed result (typically a transient lock by an editor, backup or sync tool) is retried
    /// with a FRESH store, because a store that failed to read refuses to save for the rest of its life.
    /// </summary>
    private static (SettingsStore Store, SettingsLoadResult Result) LoadSettings(
        SettingsPaths paths, SettingsStorePolicy policy, IVerifiedSettingsFile? verifiedFile, BoundedLog log)
    {
        var store = new SettingsStore(paths, policy, verifiedFile);
        var result = store.Load();
        for (var retry = 1; retry <= SettingsReadRetries && result.Status == SettingsLoadStatus.ReadFailed; retry++)
        {
            log.Add($"Settings could not be read ({result.Error}); retry {retry} of {SettingsReadRetries}.");
            Thread.Sleep(SettingsRetryDelay);
            store = new SettingsStore(paths, policy, verifiedFile);
            result = store.Load();
        }

        log.Add($"Settings: {result.Status} ({DisplayText.ForDisplay(paths.PrimaryPath)}).");
        foreach (var correction in result.Corrections)
        {
            log.Add($"Settings corrected: {correction}");
        }

        if (result.BackupPath is not null)
        {
            log.Add($"The unreadable settings file was kept as {DisplayText.ForDisplay(result.BackupPath)}.");
        }

        if (result.Error is not null)
        {
            log.Add($"Settings problem: {result.Error}");
        }

        return (store, result);
    }

    private static string ReadVersionText()
    {
        var assembly = typeof(AppHost).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit>" (source link); it is not part of the version users see.
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
