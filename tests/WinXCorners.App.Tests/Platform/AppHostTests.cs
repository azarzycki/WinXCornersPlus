using System.Reflection;
using Microsoft.Win32;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// AppHost through its test entry point: settings in a temp folder on NTFS, an in-memory (or throw-away HKLM)
/// approvals store, a throw-away Run value name. TryRestart is never called for real.
/// </summary>
public sealed class AppHostTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempDirectory _temp = new();
    private readonly SettingsPaths _paths;
    private readonly string _runValueName = "WinXCornersPlus.Test." + Guid.NewGuid().ToString("N");
    private readonly List<AppHost> _hosts = [];

    public AppHostTests(ITestOutputHelper output)
    {
        _output = output;
        _paths = SettingsPaths.ForApp(Path.Combine(_temp.Path, "Roaming"), Path.Combine(_temp.Path, "Program"));
    }

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            foreach (var line in host.Log.Snapshot())
            {
                _output.WriteLine(line);
            }

            host.Dispose();
        }

        using (var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath, writable: true))
        {
            key?.DeleteValue(_runValueName, throwOnMissingValue: false);
        }

        _temp.Dispose();
    }

    private static CommandLineOptions Options(params string[] args) => CommandLineOptions.Parse(args);

    private AppHost Create(IApprovedSettingsStore? approvals = null, bool isElevated = false, CommandLineOptions? options = null)
    {
        var host = AppHost.Create(options ?? Options(), _paths, approvals ?? new MemoryApprovalStore(), isElevated, _runValueName);
        _hosts.Add(host);
        return host;
    }

    private void WriteSettings(string path, ApplicationSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, SettingsSerializer.Serialize(settings));
    }

    private static ApplicationSettings NonDefault() => new() { HotCornersEnabled = false, TopLeftActionId = "desktop" };

    // ---- settings load paths --------------------------------------------------------------------------

    [Fact]
    public void A_valid_file_is_loaded()
    {
        WriteSettings(_paths.PrimaryPath, NonDefault());

        var host = Create();

        Assert.Equal(SettingsLoadStatus.Loaded, host.LoadResult.Status);
        Assert.False(host.Settings.Current.HotCornersEnabled);
        Assert.Equal("desktop", host.Settings.Current.TopLeftActionId);
    }

    [Fact]
    public void A_missing_file_gives_defaults_and_writes_nothing()
    {
        var host = Create();

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, host.LoadResult.Status);
        Assert.True(host.Settings.Current.HotCornersEnabled);
        Assert.False(File.Exists(_paths.PrimaryPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(_paths.PrimaryPath)));
    }

    [Fact]
    public void A_corrupt_file_is_moved_to_a_backup_with_its_bytes_preserved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.PrimaryPath)!);
        byte[] original = [0x7B, 0x20, 0x6E, 0x6F, 0x74, 0x20, 0x6A, 0x73, 0x6F, 0x6E, 0xFF, 0x00];
        File.WriteAllBytes(_paths.PrimaryPath, original);

        var host = Create();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, host.LoadResult.Status);
        Assert.NotNull(host.LoadResult.BackupPath);
        Assert.Equal(original, File.ReadAllBytes(host.LoadResult.BackupPath));
        Assert.False(File.Exists(_paths.PrimaryPath));
        Assert.Contains(host.Log.Snapshot(), line => line.Contains("was kept as", StringComparison.Ordinal));
    }

    [Fact]
    public void A_legacy_file_beside_the_exe_is_migrated_and_left_untouched()
    {
        WriteSettings(_paths.LegacyPath!, NonDefault());
        var legacyBytes = File.ReadAllBytes(_paths.LegacyPath!);

        var host = Create();

        Assert.Equal(SettingsLoadStatus.MigratedFromLegacy, host.LoadResult.Status);
        Assert.False(host.Settings.Current.HotCornersEnabled);
        Assert.True(File.Exists(_paths.PrimaryPath));
        Assert.Equal(legacyBytes, File.ReadAllBytes(_paths.LegacyPath!));
    }

    [Fact]
    public void A_file_locked_by_another_handle_is_retried_until_the_lock_goes()
    {
        WriteSettings(_paths.PrimaryPath, NonDefault());
        var locked = new FileStream(_paths.PrimaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var unlock = Task.Run(async () =>
        {
            await Task.Delay(200);
            await locked.DisposeAsync();
        });

        var host = Create();
        unlock.Wait();

        Assert.Equal(SettingsLoadStatus.Loaded, host.LoadResult.Status);
        Assert.False(host.Settings.Current.HotCornersEnabled);
        Assert.Contains(host.Log.Snapshot(), line => line.Contains("retry 1 of 3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_that_stays_locked_starts_with_defaults_and_is_never_overwritten()
    {
        WriteSettings(_paths.PrimaryPath, NonDefault());
        var before = File.ReadAllBytes(_paths.PrimaryPath);
        AppHost host;
        using (new FileStream(_paths.PrimaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            host = Create();
        }

        Assert.Equal(SettingsLoadStatus.ReadFailed, host.LoadResult.Status);
        Assert.True(host.Settings.Current.HotCornersEnabled);

        // Now unlocked: an edit in this session must still not replace the user's unread file.
        host.Settings.Update(s => s.ShowCountdown = true);
        var saved = host.Settings.Flush();
        host.RequestExit();

        Assert.False(saved.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(_paths.PrimaryPath));
    }

    // ---- other members ------------------------------------------------------------------------------

    [Fact]
    public void VersionText_is_the_informational_version_without_build_metadata()
    {
        var host = Create();
        var informational = typeof(AppHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        _output.WriteLine($"Informational version: {informational}; shown: {host.VersionText}");

        Assert.Equal(informational.Split('+')[0], host.VersionText);
        Assert.DoesNotContain('+', host.VersionText);
        Assert.Matches(@"^\d+\.\d+\.\d+", host.VersionText);
        Assert.StartsWith(typeof(AppHost).Assembly.GetName().Version!.ToString(3), host.VersionText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_security_context_is_coherent(bool isElevated)
    {
        var host = Create(isElevated: isElevated);

        var context = host.GetSecurityContext();

        Assert.Equal(isElevated, context.IsElevated);
        Assert.Equal(isElevated, host.IsElevated);
        Assert.Equal(_paths.PrimaryPath, context.SettingsPath);
        Assert.False(context.StartsWithWindows);
        Assert.Equal(host.IsStartupEnabled(), context.StartsWithWindows);
        Assert.Equal(
            WindowsPath.Normalize(AppContext.BaseDirectory).TrimEnd('\\'),
            context.Install.Directory.TrimEnd('\\'),
            ignoreCase: true);
        Assert.NotEqual(InstallLocationKind.Protected, context.Install.Kind); // Test output lives in a user folder.
    }

    [Fact]
    public void Startup_registration_uses_the_given_value_name_and_this_executable()
    {
        var host = Create();
        Assert.True(host.TrySetStartupEnabled(true, out var error), error);
        try
        {
            Assert.True(host.IsStartupEnabled());
            Assert.True(host.GetSecurityContext().StartsWithWindows);
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RunKeyPath)!;
            Assert.Equal(StartupCommandLine.Build(host.ExecutablePath), key.GetValue(_runValueName));
        }
        finally
        {
            Assert.True(host.TrySetStartupEnabled(false, out _));
        }

        Assert.False(host.IsStartupEnabled());
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/cmd.exe")]
    [InlineData("https://github.com@evil.example/")]
    [InlineData("")]
    public void OpenLink_refuses_anything_but_https_and_launches_nothing(string url)
    {
        var host = Create();

        host.OpenLink(url);

        var log = host.Log.Snapshot();
        Assert.Contains(log, line => line.Contains("] Link not opened", StringComparison.Ordinal));
        Assert.DoesNotContain(log, line => line.Contains("] Launching", StringComparison.Ordinal));
    }

    [Fact]
    public void RequestExit_saves_pending_changes_and_raises_ExitRequested()
    {
        var host = Create();
        var raised = 0;
        host.ExitRequested += () => raised++;
        host.Settings.Update(s => s.ShowCountdown = true);

        host.RequestExit();

        Assert.Equal(1, raised);
        Assert.True(SettingsSerializer.Deserialize(File.ReadAllText(_paths.PrimaryPath)).ShowCountdown);
    }

    [Fact]
    public void Dispose_is_idempotent_and_stops_the_action_worker()
    {
        var host = AppHost.Create(Options(), _paths, new MemoryApprovalStore(), false, _runValueName);
        host.Dispose();
        host.Dispose();

        host.Executor.Execute("file-explorer", new ApplicationSettings());
        Assert.Contains(host.Log.Snapshot(), line => line.Contains("shutting down", StringComparison.Ordinal));
    }

    [Fact]
    public void Unrecognized_arguments_are_logged_escaped()
    {
        var host = Create(options: Options("--bogus\u202E", "--no-elevate"));

        Assert.True(host.Options.NoElevate);
        var line = Assert.Single(host.Log.Snapshot(), l => l.Contains("] Ignored command-line argument", StringComparison.Ordinal));
        Assert.DoesNotContain('\u202E', line);
    }

    // ---- auto-elevation and the settings seal ----------------------------------------------------------

    [Theory]
    [InlineData(false, false, false, false, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, false, false, AutoElevateAction.AskBeforeElevating)]
    [InlineData(true, false, false, true, AutoElevateAction.Elevate)]
    [InlineData(true, true, false, true, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, true, true, AutoElevateAction.DoNotElevate)]
    public void DecideAutoElevate_combines_setting_rights_switch_and_approval(
        bool always, bool elevated, bool noElevate, bool approved, AutoElevateAction expected)
    {
        WriteSettings(_paths.PrimaryPath, new ApplicationSettings { AlwaysRunAsAdministrator = always });
        var approvals = new MemoryApprovalStore { Value = approved ? SettingsSeal.CreateApproval(new ApplicationSettings()) : null };

        var host = Create(approvals, elevated, noElevate ? Options("--no-elevate") : Options());

        Assert.Equal(expected, host.DecideAutoElevate());
    }

    [Fact]
    public void Not_elevated_the_seal_does_not_apply()
    {
        var host = Create(isElevated: false);
        Assert.Equal(ElevatedStartAction.NotElevated, host.EvaluateElevatedStart().Action);
    }

    [Fact]
    public void Seal_flow_first_start_asks_then_approval_is_recorded_and_an_unchanged_start_proceeds()
    {
        var approvals = new MemoryApprovalStore();
        WriteSettings(_paths.PrimaryPath, new ApplicationSettings());

        var first = Create(approvals, isElevated: true);
        var (action, seal) = first.EvaluateElevatedStart();
        Assert.Equal(ElevatedStartAction.AskFirstTimeConsent, action);
        Assert.Equal(SealStatus.NeverApproved, seal.Status);

        Assert.True(first.ApproveCurrentSettings());
        Assert.Equal(SettingsSeal.CreateApproval(first.Settings.Current), approvals.Value);

        var second = Create(approvals, isElevated: true);
        Assert.Equal(ElevatedStartAction.Proceed, second.EvaluateElevatedStart().Action);
    }

    [Fact]
    public void Seal_flow_a_tampered_settings_file_asks_with_the_differences()
    {
        var approvals = new MemoryApprovalStore();
        WriteSettings(_paths.PrimaryPath, new ApplicationSettings());
        Assert.True(Create(approvals, isElevated: true).ApproveCurrentSettings());

        // Another program, running with the user's normal rights, edits settings.json.
        var tampered = new ApplicationSettings { EnableCustomCommands = true };
        tampered.CustomCommands[2].Command = @"C:\Users\Public\payload.exe";
        WriteSettings(_paths.PrimaryPath, tampered);

        var (action, seal) = Create(approvals, isElevated: true).EvaluateElevatedStart();

        Assert.Equal(ElevatedStartAction.AskToApproveChanges, action);
        Assert.Equal(SealStatus.Changed, seal.Status);
        Assert.Contains(seal.Differences, line => line.Contains(@"C:\Users\Public\payload.exe", StringComparison.Ordinal));
    }

    [Fact]
    public void Seal_flow_edits_made_in_the_approved_session_are_re_approved_automatically()
    {
        var approvals = new MemoryApprovalStore();
        var host = Create(approvals, isElevated: true);
        Assert.True(host.ApproveCurrentSettings());
        Assert.True(host.ApproveCurrentSettings()); // A second call must not subscribe twice.
        var writes = approvals.Writes;

        host.Settings.Update(s =>
        {
            s.EnableCustomCommands = true;
            s.CustomCommands[0].Command = "notepad.exe";
        });

        Assert.Equal(writes + 1, approvals.Writes);
        Assert.Equal(SettingsSeal.CreateApproval(host.Settings.Current), approvals.Value);
        host.RequestExit();

        Assert.Equal(ElevatedStartAction.Proceed, Create(approvals, isElevated: true).EvaluateElevatedStart().Action);
    }

    [Fact]
    public void Seal_flow_without_approval_no_edit_is_approved_behind_the_users_back()
    {
        var approvals = new MemoryApprovalStore();
        var host = Create(approvals, isElevated: true);

        host.Settings.Update(s => s.AlwaysRunAsAdministrator = true);

        Assert.Null(approvals.Value);
        Assert.Equal(0, approvals.Writes);
    }

    [Fact]
    public void Seal_flow_an_approval_that_cannot_be_stored_is_reported()
    {
        var host = Create(new MemoryApprovalStore { Writable = false }, isElevated: true);

        Assert.False(host.ApproveCurrentSettings());
        Assert.Contains(host.Log.Snapshot(), line => line.Contains("could not be stored", StringComparison.Ordinal));
    }

    [SkippableFact]
    public void Seal_flow_with_the_real_HKLM_store_round_trips()
    {
        Skip.IfNot(TestEnvironment.IsElevated, TestEnvironment.NotElevatedReason);
        const string root = @"SOFTWARE\WinXCornersPlus.Tests";
        var keyPath = root + @"\" + Guid.NewGuid().ToString("N") + @"\ElevatedApprovals";
        try
        {
            var store = new ApprovedSettingsStore(keyPath);
            var first = Create(store, isElevated: true);
            Assert.Equal(ElevatedStartAction.AskFirstTimeConsent, first.EvaluateElevatedStart().Action);
            Assert.True(first.ApproveCurrentSettings());

            Assert.Equal(ElevatedStartAction.Proceed, Create(new ApprovedSettingsStore(keyPath), isElevated: true).EvaluateElevatedStart().Action);
            Assert.Equal(AutoElevateAction.DoNotElevate, Create(store, isElevated: false).DecideAutoElevate());
        }
        finally
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            hive.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
        }
    }
}
