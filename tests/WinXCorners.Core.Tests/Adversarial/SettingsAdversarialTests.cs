using System.Reflection;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Settings;

namespace WinXCorners.Core.Tests.Adversarial;

public sealed class SettingsAdversarialTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "v1-adv-" + Guid.NewGuid().ToString("N"));

    public SettingsAdversarialTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Primary => Path.Combine(_dir, "settings.json");

    // SettingsSaveResult.Succeeded is documented as "the file on disk now holds the given settings". The no-op
    // optimisation compares against what this store last wrote, not against the disk, so an external edit survives.
    [Fact]
    public void Save_RewritesTheFileAfterAnExternalEditEvenWhenSettingsAreUnchanged()
    {
        var store = new SettingsStore(new SettingsPaths(Primary, null));
        store.Load();
        var mine = ApplicationSettings.CreateDefault();
        Assert.True(store.Save(mine).Written);

        var external = ApplicationSettings.CreateDefault();
        external.EnableCustomCommands = true;
        external.CustomCommands[0].Command = @"C:\Users\alex\evil.exe";
        File.WriteAllText(Primary, SettingsSerializer.Serialize(external));

        var result = store.Save(mine);
        Assert.True(result.Succeeded);
        Assert.Equal(SettingsSerializer.Serialize(mine), File.ReadAllText(Primary));
    }

    // settings.json is attacker-writable; a lone-surrogate escape must not reach Save or the seal.
    [Fact]
    public void LoneSurrogateEscapeInSettingsFile_IsRecoveredAsCorruptNotLoaded()
    {
        File.WriteAllText(Primary, "{\"EnableCustomCommands\":true,\"CustomCommands\":[{\"Name\":\"a\",\"Command\":\"calc\\ud800.exe\"},{},{},{}]}");
        var store = new SettingsStore(new SettingsPaths(Primary, null));
        var loaded = store.Load();
        Assert.Equal(SettingsLoadStatus.RecoveredFromCorruptFile, loaded.Status);
        {
            var settings = loaded.Settings;
            settings.ShowCountdown = true; // force a real write
            var save = Record.Exception(() => store.Save(settings));
            var approve = Record.Exception(() => SettingsSeal.CreateApproval(settings));
            Assert.Null(save);
            Assert.Null(approve);
        }
    }

    public static TheoryData<string> HostileFiles() => new()
    {
        "", " ", "null", "[]", "{", "\"x\"", "1e400", "{}",
        "{\"CustomCommands\":null}",
        "{\"CustomCommands\":[null,null,null,null]}",
        "{\"CustomCommands\":[{\"Name\":null,\"Command\":null,\"Parameters\":null}]}",
        "{\"CustomCommands\":\"x\"}",
        "{\"CustomCommands\":[" + string.Join(",", Enumerable.Repeat("{}", 50_000)) + "]}",
        "{\"GlobalDelayIndex\":2147483648}",
        "{\"GlobalDelayIndex\":-2147483648}",
        "{\"HotCornerModifierKey\":\"Ctrl, Shift\"}",
        "{\"HotCornerModifierKey\":99}",
        "{\"HotCornerModifierKey\":1e10}",
        "{\"FlyoutAnimationDirection\":\" left \"}",
        "{\"TopLeftActionId\":5}",
        "{\"TopLeftActionId\":\"custom-5\"}",
        "{\"TopLeftActionId\":\"CUSTOM-1\"}",
        "{\"$id\":\"1\",\"CustomCommands\":{\"$ref\":\"1\"}}",
        "{\"$type\":\"System.IO.FileInfo\"}",
        "{\"EnableCustomCommands\":false,\"enablecustomcommands\":true}",
        new string('[', 10_000),
        "{\"a\":" + new string('[', 1000) + new string(']', 1000) + "}",
        "\uFEFF{}",
        "{\"CustomCommands\":[{\"Command\":\"" + new string('a', 900_000) + "\"}]}",
        "{\"CustomCommands\":[{\"Name\":\"\\u0000\\u202E\\u2028x\"}]}",
    };

    [Theory]
    [MemberData(nameof(HostileFiles))]
    public void Load_NeverThrowsAndReturnsSanitizedSettings(string content)
    {
        File.WriteAllText(Primary, content);
        var store = new SettingsStore(new SettingsPaths(Primary, null));
        var result = store.Load();
        Assert.NotNull(result.Settings);
        Assert.Empty(SettingsSanitizer.Sanitize(result.Settings.Clone()));
        if (result.Status == SettingsLoadStatus.RecoveredFromCorruptFile)
        {
            Assert.NotNull(result.BackupPath);
            Assert.Equal(content, File.ReadAllText(result.BackupPath!));
        }
        else
        {
            Assert.Equal(SettingsLoadStatus.Loaded, result.Status);
        }

        Assert.DoesNotContain(Directory.GetFiles(_dir), f => f.EndsWith(".tmp", StringComparison.Ordinal));
    }

    [Fact]
    public void Load_MixedCaseDuplicateKeysLetTheLastSpellingWin()
    {
        // Documented as a weakness: a human reviewing the file sees "false" first.
        File.WriteAllText(Primary, "{\"EnableCustomCommands\":false,\"enablecustomcommands\":true}");
        var result = new SettingsStore(new SettingsPaths(Primary, null)).Load();
        Assert.True(result.Settings.EnableCustomCommands);
    }

    [Fact]
    public void Save_FailureLeavesNoTempFileAndKeepsOldFile()
    {
        var blocker = Path.Combine(_dir, "sub");
        File.WriteAllText(blocker, "not a directory");
        var store = new SettingsStore(new SettingsPaths(Path.Combine(blocker, "settings.json"), null));
        store.Load();
        var result = store.Save(ApplicationSettings.CreateDefault());
        Assert.False(result.Succeeded);
        Assert.Equal("not a directory", File.ReadAllText(blocker));
        Assert.Single(Directory.GetFiles(_dir));
    }

    // SettingsMerge must copy every property the dialog edits and nothing else. Reflection catches a new property
    // being added to ApplicationSettings without a decision here.
    [Fact]
    public void Merge_CoversEveryPropertyExceptTheTrayOwnedOnes()
    {
        string[] trayOwned = ["HotCornersEnabled", "TopLeftActionId", "TopRightActionId", "BottomLeftActionId", "BottomRightActionId"];
        foreach (var property in typeof(ApplicationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var live = ApplicationSettings.CreateDefault();
            var edited = ApplicationSettings.CreateDefault();
            object changed = property.PropertyType switch
            {
                var t when t == typeof(bool) => !(bool)property.GetValue(edited)!,
                var t when t == typeof(int) => 5,
                var t when t == typeof(string) => "lock-screen",
                var t when t.IsEnum => Enum.GetValues(t).GetValue(2)!,
                _ => new[] { new CustomCommandSettings { Command = "x" }, new CustomCommandSettings(), new CustomCommandSettings(), new CustomCommandSettings() }
            };
            property.SetValue(edited, changed);
            SettingsMerge.ApplyDialogEdits(live, edited);
            var copied = SettingsSerializer.Serialize(live) != SettingsSerializer.Serialize(ApplicationSettings.CreateDefault());
            Assert.True(copied != trayOwned.Contains(property.Name), property.Name);
        }
    }

    [Fact]
    public void Session_UpdateThatThrowsStillSanitizesAndStaysDirty()
    {
        var session = new SettingsSession(new SettingsStore(new SettingsPaths(Primary, null)), ApplicationSettings.CreateDefault());
        var raised = 0;
        session.Changed += _ => raised++;
        Assert.Throws<InvalidOperationException>(() => session.Update(s =>
        {
            s.CustomCommands = null!;
            s.TopLeftActionId = "bogus";
            throw new InvalidOperationException();
        }));
        Assert.Equal("none", session.Current.TopLeftActionId);
        Assert.Equal(ApplicationSettings.CustomCommandCount, session.Current.CustomCommands.Length);
        Assert.True(session.IsDirty);
        Assert.Equal(0, raised);
    }
}
