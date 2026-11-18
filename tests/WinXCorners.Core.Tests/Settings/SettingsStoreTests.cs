using System.Text;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;

namespace WinXCorners.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private static readonly DateTimeOffset Clock = new(2026, 11, 17, 9, 30, 5, TimeSpan.FromHours(-5));
    private const string ExpectedStamp = "20261117-143005"; // UTC

    private readonly TempDirectory _directory = new();
    private readonly FixedTimeProvider _time = new(Clock);

    public SettingsStoreTests()
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

    private SettingsStore CreateStore(string? legacyPath = null) =>
        new(new SettingsPaths(PrimaryPath, legacyPath ?? LegacyPath), _time);

    private void WritePrimary(string content)
    {
        Directory.CreateDirectory(PrimaryFolder);
        File.WriteAllText(PrimaryPath, content);
    }

    private string[] FilesInPrimaryFolder() =>
        Directory.Exists(PrimaryFolder)
            ? Directory.GetFiles(PrimaryFolder).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal).ToArray()
            : [];

    private static void AssertUsable(ApplicationSettings settings)
    {
        Assert.Empty(SettingsSanitizer.Sanitize(settings.Clone()));
        Assert.Equal(ApplicationSettings.CustomCommandCount, settings.CustomCommands.Length);
        Assert.All(settings.CustomCommands, command =>
        {
            Assert.NotNull(command.Name);
            Assert.NotNull(command.Command);
            Assert.NotNull(command.Parameters);
        });
        Assert.True(ActionCatalog.IsKnown(settings.TopLeftActionId));
        Assert.True(Enum.IsDefined(settings.HotCornerModifierKey));
        Assert.InRange(settings.GlobalDelayIndex, 0, ApplicationSettings.DelayOptionCount - 1);
    }

    // ---- Load: normal paths ----

    [Fact]
    public void Load_WithNoFiles_ReturnsDefaultsAndCreatesNothing()
    {
        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, result.Status);
        SettingsAssert.Equivalent(ApplicationSettings.CreateDefault(), result.Settings);
        Assert.Empty(result.Corrections);
        Assert.Null(result.Error);
        Assert.Null(result.BackupPath);
        Assert.False(Directory.Exists(PrimaryFolder));
    }

    [Fact]
    public void Load_ValidPrimary_IsLoadedWithoutCorrections()
    {
        WritePrimary(SettingsSerializerTests.V140Json);

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Corrections);
        Assert.Null(result.Error);
        Assert.Equal("task-manager", result.Settings.TopLeftActionId);
    }

    [Fact]
    public void Load_FileWithByteOrderMark_IsLoaded()
    {
        Directory.CreateDirectory(PrimaryFolder);
        File.WriteAllText(PrimaryPath, "{\"ShowCountdown\": true}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.True(result.Settings.ShowCountdown);
    }

    // ---- Load: every review-B3 crasher ----

    [Theory]
    [InlineData("{\"CustomCommands\": [null,null,null,null]}")]
    [InlineData("{\"CustomCommands\": [{\"Name\": null, \"Command\": null, \"Parameters\": null}, {}, {}, {}]}")]
    [InlineData("{\"CustomCommands\": [{}, {}]}")]
    [InlineData("{\"CustomCommands\": [{}, {}, {}, {}, {}, {}, {}, {}, {}]}")]
    [InlineData("{\"CustomCommands\": null}")]
    [InlineData("{\"GlobalDelayIndex\": 99}")]
    [InlineData("{\"GlobalDelayIndex\": -5}")]
    [InlineData("{\"HotCornerModifierKey\": 7}")]
    [InlineData("{\"FlyoutAnimationDirection\": 42}")]
    [InlineData("{\"TopLeftActionId\": \"rm -rf\"}")]
    [InlineData("{\"TopLeftActionId\": null}")]
    public void Load_ParseableButInvalidValues_AreCorrectedAndReported(string json)
    {
        WritePrimary(json);

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.NotEmpty(result.Corrections);
        AssertUsable(result.Settings);
        Assert.Equal(json, File.ReadAllText(PrimaryPath)); // loading alone never rewrites
    }

    public static TheoryData<string> CorruptContents() => new()
    {
        "",
        "null",
        "{\"HotCornersEnabled\": true, \"TopLeftActionId\": \"file-ex",
        "{\"GlobalDelayIndex\": \"three\"}",
        "{\"CustomCommands\": \"none\"}",
        "{\"HotCornersEnabled\": 1}",
        "[1, 2, 3]",
        "this is not json",
        "{\"FlyoutAnimationDirection\": \"Diagonal\"}",
        "{" + new string(' ', 2 * 1024 * 1024) + "}" // valid JSON, but 2 MiB
    };

    [Theory]
    [MemberData(nameof(CorruptContents))]
    public void Load_CorruptFile_IsMovedToABackupWithItsOriginalBytes(string content)
    {
        WritePrimary(content);
        var originalBytes = File.ReadAllBytes(PrimaryPath);

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, result.Status);
        SettingsAssert.Equivalent(ApplicationSettings.CreateDefault(), result.Settings);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        var expectedBackup = Path.Combine(PrimaryFolder, $"settings.corrupt-{ExpectedStamp}.json");
        Assert.Equal(expectedBackup, result.BackupPath);
        Assert.Equal(originalBytes, File.ReadAllBytes(expectedBackup));
        Assert.False(File.Exists(PrimaryPath));
    }

    [Fact]
    public void Load_OversizedFile_ErrorMentionsTheLimit()
    {
        WritePrimary(new string(' ', SettingsStore.MaxFileBytes) + "{}");

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, result.Status);
        Assert.Contains("larger", result.Error);
    }

    [Fact]
    public void Load_FileExactlyAtTheLimit_IsAccepted()
    {
        WritePrimary("{}" + new string(' ', SettingsStore.MaxFileBytes - 2));

        Assert.Equal(SettingsLoadStatus.Loaded, CreateStore().Load().Status);
    }

    [Fact]
    public void Load_InvalidUtf8_IsTreatedAsCorrupt()
    {
        Directory.CreateDirectory(PrimaryFolder);
        File.WriteAllBytes(PrimaryPath, [(byte)'{', 0xC3, 0x28, (byte)'}']);

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, result.Status);
        Assert.Contains("UTF-8", result.Error);
    }

    [Fact]
    public void Load_SecondCorruptFile_DoesNotClobberTheFirstBackup()
    {
        var store = CreateStore();
        WritePrimary("first broken");
        var first = store.Load();
        WritePrimary("second broken");

        var second = store.Load();

        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.Equal("first broken", File.ReadAllText(first.BackupPath!));
        Assert.Equal("second broken", File.ReadAllText(second.BackupPath!));
        Assert.EndsWith($"settings.corrupt-{ExpectedStamp}-2.json", second.BackupPath);
    }

    [Fact]
    public void Save_AfterRecovery_WritesANewFileAndKeepsTheBackup()
    {
        WritePrimary("broken");
        var store = CreateStore();
        var loaded = store.Load();

        var saved = store.Save(loaded.Settings);

        Assert.True(saved.Succeeded);
        Assert.True(saved.Written);
        Assert.Equal("broken", File.ReadAllText(loaded.BackupPath!));
        Assert.Equal(SettingsLoadStatus.Loaded, store.Load().Status);
    }

    [Fact]
    public void Load_WhenTheBackupCannotBeMade_LeavesTheFileAndRefusesToOverwriteIt()
    {
        WritePrimary("broken");
        // Occupy every backup name the store may use so that moving the file aside must fail.
        var blockers = Enumerable.Range(1, 1000)
            .Select(attempt => Path.Combine(PrimaryFolder, attempt == 1 ? $"settings.corrupt-{ExpectedStamp}.json" : $"settings.corrupt-{ExpectedStamp}-{attempt}.json"))
            .ToArray();
        foreach (var blocker in blockers)
        {
            Directory.CreateDirectory(blocker);
        }

        var store = CreateStore();
        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, result.Status);
        Assert.Null(result.BackupPath);
        Assert.Contains("left in place", result.Error);
        Assert.Equal("broken", File.ReadAllText(PrimaryPath));

        var refused = store.Save(result.Settings);
        Assert.False(refused.Succeeded);
        Assert.NotNull(refused.Error);
        Assert.Equal("broken", File.ReadAllText(PrimaryPath));

        // Once a backup name is free again, the next save moves the file aside first and then writes.
        Directory.Delete(blockers[0]);
        var saved = store.Save(result.Settings);
        Assert.True(saved.Succeeded);
        Assert.Equal("broken", File.ReadAllText(blockers[0]));
        Assert.Equal(SettingsLoadStatus.Loaded, CreateStore().Load().Status);
    }

    [Fact]
    public void Load_WhenTheMoveItselfFails_ReportsItAndLeavesTheFile()
    {
        // A 240-character file name is legal, but adding ".corrupt-<stamp>" exceeds the 255-character
        // component limit of every common file system, so the rename fails with an I/O error.
        Directory.CreateDirectory(PrimaryFolder);
        var primary = Path.Combine(PrimaryFolder, new string('s', 235) + ".json");
        File.WriteAllText(primary, "broken");
        var store = new SettingsStore(new SettingsPaths(primary, null), _time);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, result.Status);
        Assert.Null(result.BackupPath);
        Assert.Contains("could not be moved aside", result.Error);
        Assert.Equal("broken", File.ReadAllText(primary));
        Assert.False(store.Save(result.Settings).Succeeded);
        Assert.Equal("broken", File.ReadAllText(primary));
    }

    [Fact]
    public void Save_AfterFailedBackup_ProceedsIfTheCorruptFileHasGoneAway()
    {
        WritePrimary("broken");
        foreach (var attempt in Enumerable.Range(1, 1000))
        {
            Directory.CreateDirectory(Path.Combine(PrimaryFolder, attempt == 1 ? $"settings.corrupt-{ExpectedStamp}.json" : $"settings.corrupt-{ExpectedStamp}-{attempt}.json"));
        }

        var store = CreateStore();
        var result = store.Load();
        File.Delete(PrimaryPath);

        Assert.True(store.Save(result.Settings).Succeeded);
    }

    [Fact]
    public void Load_UnreadableFile_ReturnsDefaultsAndNeverOverwritesIt()
    {
        WritePrimary("{\"ShowCountdown\": true}");
        var store = CreateStore();
        SettingsLoadResult result;
        // An exclusive handle makes the file unreadable to others on Windows and (via advisory locks) in .NET on Unix.
        using (new FileStream(PrimaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = store.Load();
        }

        Assert.Equal(SettingsLoadStatus.ReadFailed, result.Status);
        Assert.NotNull(result.Error);
        Assert.False(result.Settings.ShowCountdown);
        Assert.Equal([SettingsPaths.FileName], FilesInPrimaryFolder());

        var save = store.Save(result.Settings);
        Assert.False(save.Succeeded);
        Assert.Equal("{\"ShowCountdown\": true}", File.ReadAllText(PrimaryPath));
    }

    // ---- Legacy migration ----

    [Fact]
    public void Load_ValidLegacyFile_IsMigratedAndLeftExactlyAsItWas()
    {
        File.WriteAllText(LegacyPath, SettingsSerializerTests.V140Json);
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(LegacyPath, stamp);

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.MigratedFromLegacy, result.Status);
        Assert.Null(result.Error);
        Assert.Equal("task-manager", result.Settings.TopLeftActionId);
        Assert.Equal(SettingsSerializerTests.V140Json, File.ReadAllText(LegacyPath));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(LegacyPath));
        var migrated = CreateStore().Load();
        Assert.Equal(SettingsLoadStatus.Loaded, migrated.Status);
        SettingsAssert.Equivalent(result.Settings, migrated.Settings);
    }

    [Fact]
    public void Load_LegacyFileNeedingCorrections_IsSanitizedBeforeMigration()
    {
        File.WriteAllText(LegacyPath, "{\"CustomCommands\": [null, null, null, null], \"GlobalDelayIndex\": 99}");

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.MigratedFromLegacy, result.Status);
        Assert.NotEmpty(result.Corrections);
        AssertUsable(SettingsSerializer.Deserialize(File.ReadAllText(PrimaryPath)));
    }

    [Fact]
    public void Load_InvalidLegacyFile_GivesDefaultsAndLeavesItAlone()
    {
        File.WriteAllText(LegacyPath, "{ broken");

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, result.Status);
        Assert.Contains(LegacyPath, result.Error);
        Assert.Equal("{ broken", File.ReadAllText(LegacyPath));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(LegacyPath)!));
        Assert.False(File.Exists(PrimaryPath));
    }

    [Fact]
    public void Load_LegacyIsIgnoredWhenPrimaryExists()
    {
        File.WriteAllText(LegacyPath, "{\"ShowCountdown\": true}");
        WritePrimary("{\"ShowCountdown\": false}");

        var result = CreateStore().Load();

        Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        Assert.False(result.Settings.ShowCountdown);
    }

    [Fact]
    public void Load_MigrationWriteFailure_StillReturnsTheMigratedSettings()
    {
        File.WriteAllText(LegacyPath, "{\"ShowCountdown\": true}");
        var blocker = _directory.Combine("blocked");
        File.WriteAllText(blocker, "");
        var store = new SettingsStore(new SettingsPaths(Path.Combine(blocker, "settings.json"), LegacyPath), _time);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.MigratedFromLegacy, result.Status);
        Assert.True(result.Settings.ShowCountdown);
        Assert.NotNull(result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    public void Load_WithoutUsableLegacyPath_CreatesDefaults(bool? legacyIsPrimary)
    {
        var store = new SettingsStore(new SettingsPaths(PrimaryPath, legacyIsPrimary is null ? null : PrimaryPath), _time);

        var result = store.Load();

        Assert.Equal(SettingsLoadStatus.CreatedDefaults, result.Status);
        Assert.Null(result.Error);
    }

    // ---- Save ----

    [Fact]
    public void Save_CreatesTheFolderAndWritesUtf8WithoutBomAndNoTempFiles()
    {
        var result = CreateStore().Save(ApplicationSettings.CreateDefault());

        Assert.True(result.Succeeded);
        Assert.True(result.Written);
        Assert.Null(result.Error);
        Assert.Equal([SettingsPaths.FileName], FilesInPrimaryFolder());
        Assert.Equal((byte)'{', File.ReadAllBytes(PrimaryPath)[0]);
    }

    [Fact]
    public void Save_ReplacesAnExistingFile()
    {
        WritePrimary("{\"ShowCountdown\": false}");
        var store = CreateStore();

        var result = store.Save(new ApplicationSettings { ShowCountdown = true });

        Assert.True(result.Written);
        Assert.True(store.Load().Settings.ShowCountdown);
        Assert.Equal([SettingsPaths.FileName], FilesInPrimaryFolder());
    }

    [Fact]
    public void Save_SanitizesACopyAndNeverMutatesTheCaller()
    {
        var settings = new ApplicationSettings { GlobalDelayIndex = 99, CustomCommands = [null!] };

        var result = CreateStore().Save(settings);

        Assert.True(result.Succeeded);
        Assert.Equal(99, settings.GlobalDelayIndex);
        Assert.Single(settings.CustomCommands);
        var stored = CreateStore().Load();
        Assert.Empty(stored.Corrections);
        Assert.Equal(ApplicationSettings.DefaultDelayIndex, stored.Settings.GlobalDelayIndex);
    }

    [Fact]
    public void Save_WhenParentIsAFile_FailsWithoutThrowingOrLeavingTempFiles()
    {
        var blocker = _directory.Combine("not-a-folder");
        File.WriteAllText(blocker, "x");
        var store = new SettingsStore(new SettingsPaths(Path.Combine(blocker, "settings.json"), null), _time);

        var result = store.Save(ApplicationSettings.CreateDefault());

        Assert.False(result.Succeeded);
        Assert.False(result.Written);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal([Path.GetFileName(blocker)], Directory.GetFiles(_directory.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Save_WhenTheFinalRenameFails_RemovesTheTempFile()
    {
        // The target is a directory: the temp file can be written, but renaming over the target fails.
        Directory.CreateDirectory(PrimaryPath);

        var result = CreateStore().Save(ApplicationSettings.CreateDefault());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Empty(FilesInPrimaryFolder());
    }

    [Fact]
    public void Save_UnchangedContentAfterLoad_IsNotRewritten()
    {
        var store = CreateStore();
        Assert.True(store.Save(ApplicationSettings.CreateDefault()).Written);
        var marker = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(PrimaryPath, marker);

        var restarted = CreateStore();
        var loaded = restarted.Load();
        var result = restarted.Save(loaded.Settings);

        Assert.True(result.Succeeded);
        Assert.False(result.Written);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(PrimaryPath));
    }

    [Fact]
    public void Save_SameContentTwice_WritesOnce()
    {
        var store = CreateStore();
        var settings = new ApplicationSettings { ShowCountdown = true };

        Assert.True(store.Save(settings).Written);
        var second = store.Save(settings.Clone());

        Assert.True(second.Succeeded);
        Assert.False(second.Written);
    }

    [Fact]
    public void Save_AfterLoadingAFileThatNeededCorrections_WritesTheCorrectedVersion()
    {
        WritePrimary("{\"GlobalDelayIndex\": 99}");
        var store = CreateStore();
        var loaded = store.Load();

        var result = store.Save(loaded.Settings);

        Assert.True(result.Written);
        Assert.Empty(CreateStore().Load().Corrections);
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new SettingsStore(null!));
        Assert.ThrowsAny<ArgumentException>(() => new SettingsStore(new SettingsPaths(" ", null)));
        Assert.Throws<ArgumentNullException>(() => CreateStore().Save(null!));
        Assert.Same(LegacyPath, CreateStore().Paths.LegacyPath);
        Assert.NotNull(new SettingsStore(new SettingsPaths(PrimaryPath, null)).Paths);
    }
}
