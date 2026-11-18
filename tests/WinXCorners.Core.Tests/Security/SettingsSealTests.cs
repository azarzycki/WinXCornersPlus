using System.Text.Json;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class SettingsSealTests
{
    private static ApplicationSettings Configured()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.EnableCustomCommands = true;
        settings.TopRightActionId = ActionCatalog.CustomId(1);
        settings.CustomCommands[0].Command = "notepad.exe";
        settings.CustomCommands[1] = new CustomCommandSettings { Name = "Backup", Command = "robocopy", Parameters = "C:\\a D:\\b", LaunchHidden = true };
        settings.HotCornerModifierKey = HotCornerModifierKey.Ctrl;
        return settings;
    }

    [Fact]
    public void Check_UnchangedForSameCloneAndReorderedEquivalent()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);

        Assert.Equal(new SealCheck(SealStatus.Unchanged, []).Status, SettingsSeal.Check(settings, approval).Status);
        Assert.Empty(SettingsSeal.Check(settings, approval).Differences);
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(settings.Clone(), approval).Status);

        // Built in a different property order: the approval must not depend on it.
        var reordered = new ApplicationSettings
        {
            HotCornerModifierKey = HotCornerModifierKey.Ctrl,
            CustomCommands =
            [
                new CustomCommandSettings { LaunchHidden = false, Parameters = "", Command = "notepad.exe", Name = "" },
                new CustomCommandSettings { LaunchHidden = true, Parameters = "C:\\a D:\\b", Command = "robocopy", Name = "Backup" },
                new CustomCommandSettings(),
                new CustomCommandSettings()
            ],
            TopRightActionId = ActionCatalog.CustomId(1),
            EnableCustomCommands = true
        };
        Assert.Equal(approval, SettingsSeal.CreateApproval(reordered));
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(reordered, approval).Status);
    }

    [Fact]
    public void Check_IgnoresCosmeticAndDelayChanges()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        var changed = settings.Clone();
        changed.HotCornersEnabled = !changed.HotCornersEnabled;
        changed.StartWithWindows = !changed.StartWithWindows;
        changed.IgnoreFullScreen = !changed.IgnoreFullScreen;
        changed.AlwaysRunAsAdministrator = !changed.AlwaysRunAsAdministrator;
        changed.AlwaysHideTrayIcon = !changed.AlwaysHideTrayIcon;
        changed.FlyoutAnimationDirection = FlyoutAnimationDirection.Left;
        changed.GlobalDelayEnabled = true;
        changed.GlobalDelayIndex = 5;
        changed.TopLeftDelayEnabled = true;
        changed.TopLeftDelayIndex = 0;
        changed.TopRightDelayEnabled = true;
        changed.TopRightDelayIndex = 1;
        changed.BottomLeftDelayEnabled = true;
        changed.BottomLeftDelayIndex = 2;
        changed.BottomRightDelayEnabled = true;
        changed.BottomRightDelayIndex = 4;
        changed.ShowCountdown = true;

        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(changed, approval).Status);
        Assert.Equal(approval, SettingsSeal.CreateApproval(changed));
    }

    public static TheoryData<string, Action<ApplicationSettings>, string> SecurityRelevantChanges() => new()
    {
        { "top-left", s => s.TopLeftActionId = ActionCatalog.CustomId(0), "Top-left corner: File Explorer -> Command 1" },
        { "top-right", s => s.TopRightActionId = ActionIds.TaskManager, "Top-right corner: Command 2 (\"Backup\") -> Task Manager" },
        { "bottom-left", s => s.BottomLeftActionId = ActionIds.None, "Bottom-left corner: Show All Windows -> None" },
        { "bottom-right", s => s.BottomRightActionId = ActionIds.LockScreen, "Bottom-right corner: Action Center -> Lock Screen" },
        { "enable", s => s.EnableCustomCommands = false, "Custom commands: on -> off" },
        { "modifier", s => s.HotCornerModifierKey = HotCornerModifierKey.Alt, "Modifier key: Ctrl -> Alt" },
        { "modifier-undefined", s => s.HotCornerModifierKey = (HotCornerModifierKey)9, "Modifier key: Ctrl -> unknown value 9" },
        { "command", s => s.CustomCommands[0].Command = "cmd.exe", "Command 1 now runs: cmd.exe (was: notepad.exe)" },
        { "parameters", s => s.CustomCommands[0].Parameters = "/c evil", "Command 1 now runs: notepad.exe /c evil (was: notepad.exe)" },
        { "hidden", s => s.CustomCommands[0].LaunchHidden = true, "Command 1 window: shown -> hidden" },
        { "name", s => s.CustomCommands[1].Name = "Command 1", "Command 2 name: \"Backup\" -> \"Command 1\"" },
        { "cleared", s => s.CustomCommands[1].Command = "", "Command 2 now runs: C:\\a D:\\b (was: robocopy C:\\a D:\\b)" },
        { "slot4", s => s.CustomCommands[3].Command = "calc", "Command 4 now runs: calc (was: nothing)" }
    };

    [Theory]
    [MemberData(nameof(SecurityRelevantChanges))]
    public void Check_EachSecurityRelevantFieldIsDetected(string label, Action<ApplicationSettings> change, string expectedLine)
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        change(settings);

        var check = SettingsSeal.Check(settings, approval);
        Assert.True(check.Status == SealStatus.Changed, label);
        Assert.Equal([expectedLine], check.Differences);
    }

    [Fact]
    public void Check_ReportsEveryDifferenceAndTruncatesLongCommands()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        var longCommand = "powershell.exe -enc " + new string('A', 400);
        for (var i = 0; i < ApplicationSettings.CustomCommandCount; i++)
        {
            settings.CustomCommands[i] = new CustomCommandSettings { Command = longCommand, LaunchHidden = true, Name = "n" + i };
        }

        settings.TopLeftActionId = ActionCatalog.CustomId(3);
        var check = SettingsSeal.Check(settings, approval);

        Assert.Equal(SealStatus.Changed, check.Status);
        // 1 corner + slot 1 (name, command, window) + slot 2 (name, command) + slots 3-4 (name, command, window each).
        Assert.Equal(12, check.Differences.Count);
        Assert.All(check.Differences, line => Assert.True(line.Length < 300, line));
        Assert.Contains(check.Differences, line => line.StartsWith("Command 3 now runs: powershell.exe -enc AAAA", StringComparison.Ordinal) &&
                                                     line.Contains(DisplayText.Ellipsis + " (was: nothing)", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_EscapesLineBreaksSoACommandCannotForgeExtraApprovalLines()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        settings.CustomCommands[0].Command = "cmd.exe\nTop-left corner: File Explorer -> File Explorer";
        var check = SettingsSeal.Check(settings, approval);
        Assert.All(check.Differences, line => Assert.DoesNotContain('\n', line));
    }

    [Fact]
    public void Check_UnknownAndNoneActionIdsAreEquivalentBecauseNeitherRuns()
    {
        var settings = Configured();
        settings.BottomLeftActionId = ActionIds.None;
        var approval = SettingsSeal.CreateApproval(settings);
        settings.BottomLeftActionId = "definitely-not-an-action";
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(settings, approval).Status);
        settings.BottomLeftActionId = null!;
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(settings, approval).Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_BlankStoredApprovalIsNeverApproved(string? stored)
    {
        var check = SettingsSeal.Check(Configured(), stored);
        Assert.Equal(SealStatus.NeverApproved, check.Status);
        Assert.Empty(check.Differences);
    }

    public static TheoryData<string> GarbageApprovals()
    {
        var valid = SettingsSeal.CreateApproval(ApplicationSettings.CreateDefault());
        return new TheoryData<string>
        {
            "garbage",
            "{",
            "[]",
            "null",
            "42",
            "{}",
            valid.Replace("\"version\":1", "\"version\":0", StringComparison.Ordinal),
            valid.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal),
            valid.Replace("\"version\":1", "\"version\":\"1\"", StringComparison.Ordinal),
            valid.Replace("\"version\":1", "\"version\":1.5", StringComparison.Ordinal),
            valid.Replace("WinXCornersPlus.SettingsApproval", "Other", StringComparison.Ordinal),
            valid.Replace("\"topLeft\":", "\"topLeftX\":", StringComparison.Ordinal),
            valid.Replace("\"customCommands\":false", "\"customCommands\":0", StringComparison.Ordinal),
            valid.Replace("\"modifier\":0", "\"modifier\":\"None\"", StringComparison.Ordinal),
            valid.Replace("\"modifier\":0", "\"modifier\":1e100", StringComparison.Ordinal),
            valid.Replace("\"commands\":[", "\"commands\":[{},", StringComparison.Ordinal),
            valid.Replace("\"commands\":[", "\"commands\":[1,", StringComparison.Ordinal),
            valid.Replace("\"commands\":[", "\"commands\":{\"x\":[", StringComparison.Ordinal) + "}",
            valid.Replace(",\"hidden\":false}]", "}]", StringComparison.Ordinal),
            valid.Replace("\"name\":\"\"", "\"name\":null", StringComparison.Ordinal),
            // An older, unversioned format: a bare snapshot without format/version fields.
            "{\"topLeft\":\"file-explorer\",\"topRight\":\"none\"}"
        };
    }

    [Theory]
    [MemberData(nameof(GarbageApprovals))]
    public void Check_UnparsableOrUnknownVersionIsUnreadable(string stored)
    {
        var check = SettingsSeal.Check(ApplicationSettings.CreateDefault(), stored);
        Assert.Equal(SealStatus.Unreadable, check.Status);
        Assert.Equal([SettingsSeal.UnreadableDifference], check.Differences);
    }

    [Fact]
    public void CreateApproval_IsVersionedJsonContainingOnlyTheSnapshot()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        using var document = JsonDocument.Parse(approval);
        var names = document.RootElement.EnumerateObject().Select(static p => p.Name).ToArray();
        Assert.Equal(["format", "version", "topLeft", "topRight", "bottomLeft", "bottomRight", "customCommands", "modifier", "commands"], names);
        Assert.Equal(SettingsSeal.FormatVersion, document.RootElement.GetProperty("version").GetInt32());
        var commandFields = document.RootElement.GetProperty("commands")[0].EnumerateObject().Select(static p => p.Name).ToArray();
        Assert.Equal(["name", "command", "parameters", "hidden"], commandFields);
        Assert.DoesNotContain("Delay", approval, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tray", approval, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateApproval_IgnoresCommandSlotsBeyondTheFourThatCanRun()
    {
        var settings = Configured();
        var approval = SettingsSeal.CreateApproval(settings);
        settings.CustomCommands = [.. settings.CustomCommands, new CustomCommandSettings { Command = "extra.exe" }];
        Assert.Equal(approval, SettingsSeal.CreateApproval(settings));
    }

    [Fact]
    public void CapturedSnapshotsCompareByValue()
    {
        var a = SettingsSeal.Capture(Configured());
        var b = SettingsSeal.Capture(Configured());
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.False(a.Equals(null));
        Assert.NotEqual(a, b with { EnableCustomCommands = false });
        Assert.Equal(ApplicationSettings.CustomCommandCount, a.Commands.Count);
    }

    [Fact]
    public void UnsanitizedSettingsDoNotThrow()
    {
        var hostile = new ApplicationSettings
        {
            TopLeftActionId = null!,
            TopRightActionId = null!,
            CustomCommands = [null!, new CustomCommandSettings { Name = null!, Command = null!, Parameters = null! }]
        };
        var approval = SettingsSeal.CreateApproval(hostile);
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(hostile, approval).Status);
        Assert.NotEmpty(SettingsSeal.Summarize(hostile));

        var nullArray = new ApplicationSettings { CustomCommands = null! };
        var nullArrayCheck = SettingsSeal.Check(nullArray, approval);
        Assert.Equal(SealStatus.Changed, nullArrayCheck.Status);
        Assert.Equal(["Top-left corner: None -> File Explorer", "Top-right corner: None -> Hide Other Windows"], nullArrayCheck.Differences);
    }

    [Fact]
    public void Summarize_ListsCornersSwitchesAndConfiguredCommands()
    {
        var lines = SettingsSeal.Summarize(Configured());
        Assert.Equal(
        [
            "Top-left corner: File Explorer",
            "Top-right corner: Command 2 (\"Backup\")",
            "Bottom-left corner: Show All Windows",
            "Bottom-right corner: Action Center",
            "Custom commands: on",
            "Modifier key: Ctrl",
            "Command 1 runs: notepad.exe",
            "Command 2 (\"Backup\") runs: robocopy C:\\a D:\\b (hidden window)"
        ], lines);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsSeal.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => SettingsSeal.Check(null!, "x"));
    }
}
