using System.Text;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;

namespace WinXCorners.Core.Tests.Settings;

/// <summary>
/// The elevated policy (review V2 D1): an elevated store never moves, backs up or migrates anything, reads and
/// writes only through the verified file access, and blocks saving whenever it could not read the file safely.
/// The Windows side of the verification (junctions, symbolic links, hard links) is tested in WinXCorners.App.Tests.
/// </summary>
public sealed class SettingsStoreElevatedTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public SettingsStoreElevatedTests()
    {
        PrimaryFolder = _directory.Combine("roaming", SettingsPaths.FolderName);
        PrimaryPath = Path.Combine(PrimaryFolder, SettingsPaths.FileName);
        LegacyPath = _directory.Combine("app", SettingsPaths.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(LegacyPath)!);
    }

    private string PrimaryFolder { get; }

    private string PrimaryPath { get; }

    private string LegacyPath { get; }

    public void Dispose() => _directory.Dispose();

    /// <summary>Reads and writes the real file in place, like the app's verified access does on an unredirected folder.</summary>
    private sealed class InPlaceFile(string path) : IVerifiedSettingsFile
    {
        public int Reads { get; private set; }

        public List<byte[]> Writes { get; } = [];

        public VerifiedRead Read(int maxBytes)
        {
            Reads++;
            if (!File.Exists(path))
            {
                return new VerifiedRead(VerifiedReadStatus.Missing);
            }

            var bytes = File.ReadAllBytes(path);
            return new VerifiedRead(VerifiedReadStatus.Read, bytes.Length > maxBytes ? bytes[..maxBytes] : bytes);
        }

        public string? Write(byte[] content)
        {
            Writes.Add(content);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            return null;
        }
    }

    /// <summary>Always answers the same way; records whether a write was attempted.</summary>
    private sealed class ScriptedFile(VerifiedRead read, string? writeError = null) : IVerifiedSettingsFile
    {
        public int Writes { get; private set; }

        public VerifiedRead Read(int maxBytes) => read;

        public string? Write(byte[] content)
        {
            Writes++;
            return writeError;
        }
    }

    private SettingsStore Elevated(IVerifiedSettingsFile file) =>
        new(new SettingsPaths(PrimaryPath, LegacyPath), SettingsStorePolicy.For(isElevated: true), file);

    private string[] Snapshot() =>
        Directory.GetFiles(_directory.Path, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_directory.Path, path) + ":" + Convert.ToHexString(File.ReadAllBytes(path)))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private void WritePrimary(byte[] bytes)
    {
        Directory.CreateDirectory(PrimaryFolder);
        File.WriteAllBytes(PrimaryPath, bytes);
    }

    // ---- the policy itself ----------------------------------------------------------------------

    [Fact]
    public void For_elevated_forbids_every_move_backup_and_migration_and_requires_verified_access()
    {
        var policy = SettingsStorePolicy.For(isElevated: true);

        Assert.True(policy.IsElevatedSession);
        Assert.False(policy.MoveUnreadableFileAside);
        Assert.False(policy.MigrateLegacyFile);
        Assert.False(policy.WriteThroughTemporaryFile);
        Assert.True(policy.RequireVerifiedFileAccess);
        Assert.Same(SettingsStorePolicy.Elevated, policy);
    }

    [Fact]
    public void For_normal_keeps_recovery_migration_and_atomic_saves()
    {
        var policy = SettingsStorePolicy.For(isElevated: false);

        Assert.False(policy.IsElevatedSession);
        Assert.True(policy.MoveUnreadableFileAside);
        Assert.True(policy.MigrateLegacyFile);
        Assert.True(policy.WriteThroughTemporaryFile);
        Assert.False(policy.RequireVerifiedFileAccess);
        Assert.Same(SettingsStorePolicy.Normal, policy);
        Assert.Same(SettingsStorePolicy.Normal, new SettingsStore(new SettingsPaths(PrimaryPath, null)).Policy);
    }

    [Fact]
    public void An_elevated_store_cannot_be_built_without_verified_access()
    {
        var paths = new SettingsPaths(PrimaryPath, LegacyPath);
        Assert.Throws<ArgumentException>(() => new SettingsStore(paths, SettingsStorePolicy.Elevated, null));
        Assert.Throws<ArgumentNullException>(() => new SettingsStore(paths, null!, null));
    }

    [Fact]
    public void A_normal_store_ignores_a_verified_access_it_was_given()
    {
        WritePrimary(Encoding.UTF8.GetBytes("not json"));
        var file = new ScriptedFile(new VerifiedRead(VerifiedReadStatus.Refused, Error: "never asked"));
        var store = new SettingsStore(new SettingsPaths(PrimaryPath, LegacyPath), SettingsStorePolicy.Normal, file);

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, store.Load().Status);
        Assert.True(store.Save(new ApplicationSettings()).Succeeded);
        Assert.Equal(0, file.Writes);
    }

    // ---- load -----------------------------------------------------------------------------------

    [Fact]
    public void A_valid_file_is_loaded_through_the_verified_access()
    {
        WritePrimary(Encoding.UTF8.GetBytes(SettingsSerializer.Serialize(new ApplicationSettings { HotCornersEnabled = false })));
        var file = new InPlaceFile(PrimaryPath);

        var result = Elevated(file).Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.False(result.Settings.HotCornersEnabled);
        Assert.Equal(1, file.Reads);
    }

    [Theory]
    [InlineData(new byte[] { 0x7B, 0xFF, 0x00 })] // not UTF-8
    [InlineData(new byte[] { 0x7B, 0x20, 0x6E })] // "{ n": not JSON
    public void An_invalid_file_is_kept_untouched_and_blocks_saving(byte[] content)
    {
        WritePrimary(content);
        File.WriteAllText(LegacyPath, SettingsSerializer.Serialize(new ApplicationSettings()));
        var before = Snapshot();
        var file = new InPlaceFile(PrimaryPath);
        var store = Elevated(file);

        var result = store.Load();
        var saved = store.Save(new ApplicationSettings { ShowCountdown = true });

        Assert.Equal(SettingsLoadStatus.KeptInvalidFile, result.Status);
        Assert.Null(result.BackupPath);
        Assert.Contains("left untouched", result.Error, StringComparison.Ordinal);
        Assert.Contains("without administrator rights", result.Error, StringComparison.Ordinal);
        AssertDefaults(result.Settings);
        Assert.False(saved.Succeeded);
        Assert.Empty(file.Writes);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void An_oversized_file_is_kept_untouched()
    {
        WritePrimary(new byte[SettingsStore.MaxFileBytes + 10]);
        var before = Snapshot();

        var result = Elevated(new InPlaceFile(PrimaryPath)).Load();

        Assert.Equal(SettingsLoadStatus.KeptInvalidFile, result.Status);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void A_redirected_location_is_refused_reported_and_never_written()
    {
        var file = new ScriptedFile(new VerifiedRead(VerifiedReadStatus.Refused, Error: "the settings folder is a junction."));
        var store = Elevated(file);

        var result = store.Load();
        var saved = store.Save(new ApplicationSettings());

        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, result.Status);
        Assert.Contains("the settings folder is a junction.", result.Error, StringComparison.Ordinal);
        Assert.Contains(SettingsStore.FixFromNormalSessionText, result.Error, StringComparison.Ordinal);
        AssertDefaults(result.Settings);
        Assert.False(saved.Succeeded);
        Assert.Contains("redirected", saved.Error, StringComparison.Ordinal);
        Assert.Equal(0, file.Writes);
    }

    [Fact]
    public void A_refusal_without_a_reason_still_refuses()
    {
        var result = Elevated(new ScriptedFile(new VerifiedRead(VerifiedReadStatus.Refused))).Load();
        Assert.Equal(SettingsLoadStatus.RefusedRedirectedLocation, result.Status);
        Assert.Contains("redirected", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VerifiedReadStatus.Failed, "sharing violation")]
    [InlineData(VerifiedReadStatus.Failed, null)]
    [InlineData(VerifiedReadStatus.Read, null)] // "read" but no content: an implementation bug, treated as unreadable
    [InlineData((VerifiedReadStatus)99, null)]
    public void A_failed_read_is_ReadFailed_and_blocks_saving(VerifiedReadStatus status, string? error)
    {
        var file = new ScriptedFile(new VerifiedRead(status, Error: error));
        var store = Elevated(file);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.ReadFailed, result.Status);
        Assert.Contains(SettingsStore.FixFromNormalSessionText, result.Error, StringComparison.Ordinal);
        Assert.False(store.Save(new ApplicationSettings()).Succeeded);
        Assert.Equal(0, file.Writes);
    }

    [Fact]
    public void A_missing_file_gives_defaults_and_the_legacy_file_is_not_migrated_or_even_read()
    {
        File.WriteAllText(LegacyPath, SettingsSerializer.Serialize(new ApplicationSettings { HotCornersEnabled = false }));
        var before = Snapshot();

        var result = Elevated(new InPlaceFile(PrimaryPath)).Load();

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, result.Status);
        Assert.True(result.Settings.HotCornersEnabled);
        Assert.Equal(before, Snapshot());
        Assert.False(Directory.Exists(PrimaryFolder));
    }

    // ---- save -----------------------------------------------------------------------------------

    [Fact]
    public void Saving_writes_in_place_through_the_verified_access_with_no_temp_file()
    {
        var file = new InPlaceFile(PrimaryPath);
        var store = Elevated(file);
        store.Load();

        var saved = store.Save(new ApplicationSettings { ShowCountdown = true });

        Assert.True(saved.Succeeded);
        Assert.True(saved.Written);
        var written = Assert.Single(file.Writes);
        Assert.Equal(written, File.ReadAllBytes(PrimaryPath));
        Assert.True(SettingsSerializer.Deserialize(Encoding.UTF8.GetString(written)).ShowCountdown);
        Assert.Equal([SettingsPaths.FileName], Directory.GetFiles(PrimaryFolder).Select(Path.GetFileName));
    }

    [Fact]
    public void Saving_identical_content_writes_nothing()
    {
        var file = new InPlaceFile(PrimaryPath);
        var store = Elevated(file);
        store.Load();
        Assert.True(store.Save(new ApplicationSettings()).Written);

        var again = store.Save(new ApplicationSettings());

        Assert.True(again.Succeeded);
        Assert.False(again.Written);
        Assert.Single(file.Writes);
    }

    [Fact]
    public void A_longer_file_on_disk_is_rewritten()
    {
        var file = new InPlaceFile(PrimaryPath);
        var store = Elevated(file);
        store.Load();
        store.Save(new ApplicationSettings());
        File.AppendAllText(PrimaryPath, "   ");

        Assert.True(store.Save(new ApplicationSettings()).Written);
        Assert.Equal(2, file.Writes.Count);
    }

    [Fact]
    public void A_write_error_is_reported_and_the_session_can_retry()
    {
        var file = new ScriptedFile(new VerifiedRead(VerifiedReadStatus.Missing), writeError: "the settings file is a hard link.");
        var store = Elevated(file);
        Assert.Equal(SettingsLoadStatus.CreatedDefaults, store.Load().Status);

        var saved = store.Save(new ApplicationSettings());

        Assert.False(saved.Succeeded);
        Assert.Equal("the settings file is a hard link.", saved.Error);
        Assert.False(store.Save(new ApplicationSettings()).Succeeded);
        Assert.Equal(2, file.Writes);
    }

    private static void AssertDefaults(ApplicationSettings settings) =>
        SettingsAssert.Equivalent(ApplicationSettings.CreateDefault(), settings);
}
