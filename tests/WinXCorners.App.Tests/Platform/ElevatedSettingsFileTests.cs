using System.Diagnostics;
using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;
using Xunit.Abstractions;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// Review V2 D1 on real NTFS: a program without administrator rights replaces %APPDATA%\WinXCornersPlus (or the file in
/// it) with a junction, a symbolic link or a hard link. An elevated session must refuse to follow it, move nothing,
/// create nothing outside, and say so; a normal-rights session behaves exactly as before. The links are created by the
/// (administrator) test runner; the checks do not depend on who created them. Redirection Guard is not on in the test
/// process, so these tests exercise the handle checks alone.
/// </summary>
public sealed class ElevatedSettingsFileTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly TempDirectory _temp = new();
    private readonly List<AppHost> _hosts = [];
    private readonly string _roaming;
    private readonly string _folder;
    private readonly string _elsewhere;
    private readonly SettingsPaths _paths;
    private readonly string _runValueName = "WinXCornersPlus.Test." + Guid.NewGuid().ToString("N");

    public ElevatedSettingsFileTests(ITestOutputHelper output)
    {
        _output = output;
        _roaming = Path.Combine(_temp.Path, "Roaming");
        _folder = Path.Combine(_roaming, SettingsPaths.FolderName);
        _elsewhere = Path.Combine(_temp.Path, "Elsewhere");
        Directory.CreateDirectory(_roaming);
        Directory.CreateDirectory(_elsewhere);
        _paths = SettingsPaths.ForApp(_roaming, Path.Combine(_temp.Path, "Program"));
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

        // Remove links first so that deleting the temp folder never follows one.
        foreach (var path in Directory.EnumerateFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories).OrderByDescending(static p => p.Length))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(path);
                }
                else
                {
                    File.Delete(path);
                }
            }
        }

        _temp.Dispose();
    }

    private AppHost Create(bool isElevated)
    {
        var host = AppHost.Create(CommandLineOptions(), _paths, new MemoryApprovalStore(), isElevated, _runValueName);
        _hosts.Add(host);
        return host;
    }

    private static WinXCorners.Core.Platform.CommandLineOptions CommandLineOptions() => WinXCorners.Core.Platform.CommandLineOptions.Parse([]);

    private static byte[] Valid(bool hotCorners = false) =>
        System.Text.Encoding.UTF8.GetBytes(SettingsSerializer.Serialize(new ApplicationSettings { HotCornersEnabled = hotCorners, TopLeftActionId = "desktop" }));

    private static readonly byte[] Corrupt = [0x7B, 0x20, 0x6E, 0x6F, 0x74, 0xFF, 0x00];

    /// <summary>Every file under <paramref name="root"/> (not following links) with its bytes, for before/after comparisons.</summary>
    private static string[] Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Where(static f => (File.GetAttributes(f) & FileAttributes.ReparsePoint) == 0)
            .Select(f => Path.GetRelativePath(root, f) + ":" + Convert.ToHexString(File.ReadAllBytes(f)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static void Cmd(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c " + arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.SystemDirectory
        })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30_000), "cmd hung");
        Assert.True(process.ExitCode == 0, $"cmd {arguments} failed: {output}");
    }

    private void MakeJunction(string link, string target) => Cmd($"mklink /J \"{link}\" \"{target}\"");

    private void MakeHardLink(string link, string target) => Cmd($"mklink /H \"{link}\" \"{target}\"");

    private void AssertSaveRefused(AppHost host)
    {
        host.Settings.Update(static s => s.ShowCountdown = !s.ShowCountdown);
        var saved = host.Settings.Flush();
        host.RequestExit();
        Assert.False(saved.Succeeded, "An elevated session saved through a redirected location.");
    }

    // ---- junction and directory symbolic link in place of the settings folder ------------------------

    public static TheoryData<string> FolderLinkKinds => new() { "junction", "directory symlink" };

    private void LinkFolder(string kind)
    {
        if (kind == "junction")
        {
            MakeJunction(_folder, _elsewhere);
        }
        else
        {
            Directory.CreateSymbolicLink(_folder, _elsewhere);
        }
    }

    [Theory]
    [MemberData(nameof(FolderLinkKinds))]
    public void Elevated_a_redirected_settings_folder_is_refused_nothing_is_moved_or_created(string kind)
    {
        File.WriteAllBytes(Path.Combine(_elsewhere, SettingsPaths.FileName), Corrupt);
        LinkFolder(kind);
        var before = Snapshot(_elsewhere);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, host.LoadResult.Status);
        Assert.Contains("reparse point", host.LoadResult.Error, StringComparison.Ordinal);
        Assert.Contains("without administrator rights", host.LoadResult.Error, StringComparison.Ordinal);
        Assert.Equal(new ApplicationSettings().HotCornersEnabled, host.Settings.Current.HotCornersEnabled);
        AssertSaveRefused(host);
        Assert.Equal(before, Snapshot(_elsewhere));
        Assert.Single(Directory.GetFiles(_elsewhere)); // No backup, no temp file, no new settings file.
    }

    [Theory]
    [MemberData(nameof(FolderLinkKinds))]
    public void Elevated_a_redirected_empty_settings_folder_is_not_written_into(string kind)
    {
        LinkFolder(kind);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, host.LoadResult.Status);
        AssertSaveRefused(host);
        Assert.Empty(Directory.GetFileSystemEntries(_elsewhere));
    }

    [Theory]
    [MemberData(nameof(FolderLinkKinds))]
    public void Normal_rights_follow_a_redirected_folder_exactly_as_before(string kind)
    {
        File.WriteAllBytes(Path.Combine(_elsewhere, SettingsPaths.FileName), Corrupt);
        LinkFolder(kind);

        var host = Create(isElevated: false);

        // Unchanged behaviour: the corrupt file is moved aside (inside the link target) and defaults are used.
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, host.LoadResult.Status);
        Assert.NotNull(host.LoadResult.BackupPath);
        Assert.Contains(Directory.GetFiles(_elsewhere), static f => Path.GetFileName(f).StartsWith("settings.corrupt-", StringComparison.Ordinal));
    }

    [Fact]
    public void Elevated_a_redirected_parent_folder_is_refused()
    {
        // The settings folder itself is real, but %APPDATA% (its parent) leads somewhere else.
        var realRoaming = Path.Combine(_temp.Path, "RealRoaming");
        Directory.CreateDirectory(Path.Combine(realRoaming, SettingsPaths.FolderName));
        File.WriteAllBytes(Path.Combine(realRoaming, SettingsPaths.FolderName, SettingsPaths.FileName), Valid());
        Directory.Delete(_roaming);
        MakeJunction(_roaming, realRoaming);
        var before = Snapshot(realRoaming);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, host.LoadResult.Status);
        AssertSaveRefused(host);
        Assert.Equal(before, Snapshot(realRoaming));
    }

    // ---- symbolic link and hard link in place of settings.json ------------------------------------------

    [Fact]
    public void Elevated_a_symbolic_link_in_place_of_the_file_is_refused_and_its_target_untouched()
    {
        Directory.CreateDirectory(_folder);
        var target = Path.Combine(_elsewhere, "target.json");
        File.WriteAllBytes(target, Valid());
        File.CreateSymbolicLink(Path.Combine(_folder, SettingsPaths.FileName), target);
        var before = Snapshot(_temp.Path);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, host.LoadResult.Status);
        Assert.Contains("symbolic link", host.LoadResult.Error, StringComparison.Ordinal);
        Assert.True(host.Settings.Current.HotCornersEnabled, "The link's target was read.");
        AssertSaveRefused(host);
        Assert.Equal(before, Snapshot(_temp.Path));
    }

    [Fact]
    public void Elevated_a_hard_link_in_place_of_the_file_is_refused_and_both_names_untouched()
    {
        Directory.CreateDirectory(_folder);
        var target = Path.Combine(_elsewhere, "target.json");
        File.WriteAllBytes(target, Valid());
        MakeHardLink(Path.Combine(_folder, SettingsPaths.FileName), target);
        var before = Snapshot(_temp.Path);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, host.LoadResult.Status);
        Assert.Contains("hard links", host.LoadResult.Error, StringComparison.Ordinal);
        AssertSaveRefused(host);
        Assert.Equal(before, Snapshot(_temp.Path));
    }

    [Fact]
    public void Normal_rights_follow_a_file_symbolic_link_exactly_as_before()
    {
        Directory.CreateDirectory(_folder);
        var target = Path.Combine(_elsewhere, "target.json");
        File.WriteAllBytes(target, Valid());
        File.CreateSymbolicLink(Path.Combine(_folder, SettingsPaths.FileName), target);

        var host = Create(isElevated: false);

        Assert.Equal(SettingsLoadStatus.Loaded, host.LoadResult.Status);
        Assert.False(host.Settings.Current.HotCornersEnabled);
    }

    // ---- an unredirected folder: what an elevated session may do -----------------------------------------

    [Fact]
    public void Elevated_a_corrupt_file_is_kept_untouched_with_no_backup_and_saving_blocked()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, SettingsPaths.FileName), Corrupt);
        var before = Snapshot(_temp.Path);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.KeptInvalidFile, host.LoadResult.Status);
        Assert.Null(host.LoadResult.BackupPath);
        Assert.Contains("without administrator rights", host.LoadResult.Error, StringComparison.Ordinal);
        AssertSaveRefused(host);
        Assert.Equal(before, Snapshot(_temp.Path));
    }

    [Fact]
    public void Elevated_the_legacy_file_is_not_migrated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.LegacyPath)!);
        File.WriteAllBytes(_paths.LegacyPath!, Valid());
        var before = Snapshot(_temp.Path);

        var host = Create(isElevated: true);

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, host.LoadResult.Status);
        Assert.True(host.Settings.Current.HotCornersEnabled);
        Assert.Equal(before, Snapshot(_temp.Path));
        Assert.False(Directory.Exists(_folder));
    }

    [Fact]
    public void Elevated_saves_create_the_folder_and_file_in_place_and_rewrite_them_without_temp_files()
    {
        var host = Create(isElevated: true);
        Assert.Equal(SettingsLoadStatus.CreatedDefaults, host.LoadResult.Status);

        host.Settings.Update(static s => s.TopLeftActionId = "desktop");
        Assert.True(host.Settings.Flush().Succeeded);
        host.Settings.Update(static s => s.ShowCountdown = true);
        Assert.True(host.Settings.Flush().Succeeded);
        host.Settings.Update(static s => s.TopLeftActionId = "none"); // shorter content: the old tail must go
        Assert.True(host.Settings.Flush().Succeeded);

        Assert.Equal([SettingsPaths.FileName], Directory.GetFiles(_folder).Select(Path.GetFileName));
        var saved = SettingsSerializer.Deserialize(File.ReadAllText(_paths.PrimaryPath));
        Assert.Equal("none", saved.TopLeftActionId);
        Assert.True(saved.ShowCountdown);

        var again = Create(isElevated: true);
        Assert.Equal(SettingsLoadStatus.Loaded, again.LoadResult.Status);
        Assert.Equal("none", again.Settings.Current.TopLeftActionId);
    }

    [Fact]
    public void The_verified_access_reports_missing_and_reads_through_short_names()
    {
        var file = new VerifiedSettingsFile(_paths.PrimaryPath);
        Assert.Equal(VerifiedReadStatus.Missing, file.Read(100).Status);

        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(_paths.PrimaryPath, Valid());
        var read = file.Read(1 << 20);
        Assert.Equal(VerifiedReadStatus.Read, read.Status);
        Assert.Equal(Valid(), read.Content);
        Assert.Equal(10, file.Read(10).Content!.Length);

        var missingRoot = new VerifiedSettingsFile(Path.Combine(_temp.Path, "NoSuchRoaming", SettingsPaths.FolderName, SettingsPaths.FileName));
        Assert.Equal(VerifiedReadStatus.Missing, missingRoot.Read(100).Status);
        Assert.Contains("does not exist", missingRoot.Write([1, 2, 3]), StringComparison.Ordinal);
    }
}
