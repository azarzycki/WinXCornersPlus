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
        { "top-right", s => s.TopRightActionId = ActionIds.TaskManager, "Top-right corner: Command 2 (“Backup”) -> Task Manager" },
        { "bottom-left", s => s.BottomLeftActionId = ActionIds.None, "Bottom-left corner: Show All Windows -> None" },
        { "bottom-right", s => s.BottomRightActionId = ActionIds.LockScreen, "Bottom-right corner: Action Center -> Lock Screen" },
        { "enable", s => s.EnableCustomCommands = false, "Custom commands: on -> off" },
        { "modifier", s => s.HotCornerModifierKey = HotCornerModifierKey.Alt, "Modifier key: Ctrl -> Alt" },
        { "modifier-undefined", s => s.HotCornerModifierKey = (HotCornerModifierKey)9, "Modifier key: Ctrl -> unknown value 9" },
        { "tray", s => s.AlwaysHideTrayIcon = true, "Tray icon: shown -> hidden" },
        { "command", s => s.CustomCommands[0].Command = "cmd.exe", "Command 1 now runs: program “cmd.exe” (was: program “notepad.exe”)" },
        { "parameters", s => s.CustomCommands[0].Parameters = "/c evil", "Command 1 now runs: program “notepad.exe” with arguments “/c evil” (was: program “notepad.exe”)" },
        { "hidden", s => s.CustomCommands[0].LaunchHidden = true, "Command 1 window: shown -> hidden" },
        { "name", s => s.CustomCommands[1].Name = "Command 1", "Command 2 name: “Backup” -> “Command 1”" },
        { "cleared", s => s.CustomCommands[1].Command = "", "Command 2 now runs: program “” with arguments “C:\\a D:\\b” (was: program “robocopy” with arguments “C:\\a D:\\b”)" },
        { "whitespace", s => s.CustomCommands[0].Command = "notepad.exe ", "Command 1 now runs: program “notepad.exe ” (was: program “notepad.exe”)" },
        { "slot4", s => s.CustomCommands[3].Command = "calc", "Command 4 now runs: program “calc” (was: nothing)" }
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
    public void Check_ReportsEveryDifferenceAndShowsLongCommandsInFull()
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
        Assert.Contains("Command 3 now runs: program “" + longCommand + "” (was: nothing)", check.Differences);
        Assert.DoesNotContain(check.Differences, line => line.Contains(DisplayText.Ellipsis, StringComparison.Ordinal));
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
            valid.Replace("\"version\":2", "\"version\":1", StringComparison.Ordinal),
            valid.Replace("\"version\":2", "\"version\":3", StringComparison.Ordinal),
            valid.Replace("\"version\":2", "\"version\":\"2\"", StringComparison.Ordinal),
            valid.Replace("\"version\":2", "\"version\":2.0", StringComparison.Ordinal),
            valid.Replace("\"version\":2", "\"version\":2.5", StringComparison.Ordinal),
            // Not what the writer produces: whitespace, alternative escapes, reordered or missing fields.
            " " + valid,
            valid.Replace(",", ", ", StringComparison.Ordinal),
            valid.Replace("\"topLeft\":\"file-explorer\"", "\"topLeft\":\"file\\u002Dexplorer\"", StringComparison.Ordinal),
            valid.Replace("\"topLeft\":\"file-explorer\"", "\"topLeft\":\"file\\/explorer\"", StringComparison.Ordinal),
            valid.Replace("\"name\":\"\"", "\"name\":\"\\n\"", StringComparison.Ordinal),
            valid.Replace("\"name\":\"\"", "\"name\":\"\\u000a\"", StringComparison.Ordinal),
            valid.Replace("\"name\":\"\"", "\"name\":\"\u00e9\"", StringComparison.Ordinal),
            valid.Replace(",\"hideTrayIcon\":false", string.Empty, StringComparison.Ordinal),
            valid.Replace("\"customCommands\":false,\"modifier\":0", "\"modifier\":0,\"customCommands\":false", StringComparison.Ordinal),
            valid.Replace("\"modifier\":0", "\"modifier\":-0", StringComparison.Ordinal),
            valid.Replace("\"commands\":[", "\"extra\":1,\"commands\":[", StringComparison.Ordinal),
            // A version-1 approval (no tray icon field) must be approved again.
            "{\"format\":\"WinXCornersPlus.SettingsApproval\",\"version\":1,\"topLeft\":\"file-explorer\",\"topRight\":\"hide-other-windows\"," +
            "\"bottomLeft\":\"all-windows\",\"bottomRight\":\"action-center\",\"customCommands\":false,\"modifier\":0,\"commands\":[" +
            string.Join(",", Enumerable.Repeat("{\"name\":\"\",\"command\":\"\",\"parameters\":\"\",\"hidden\":false}", 4)) + "]}",
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
        Assert.Equal(["format", "version", "topLeft", "topRight", "bottomLeft", "bottomRight", "customCommands", "modifier", "hideTrayIcon", "commands"], names);
        Assert.Equal(SettingsSeal.FormatVersion, document.RootElement.GetProperty("version").GetInt32());
        var commandFields = document.RootElement.GetProperty("commands")[0].EnumerateObject().Select(static p => p.Name).ToArray();
        Assert.Equal(["name", "command", "parameters", "hidden"], commandFields);
        Assert.DoesNotContain("Delay", approval, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Animation", approval, StringComparison.OrdinalIgnoreCase);
        Assert.All(approval, c => Assert.InRange(c, ' ', '~'));
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
            "Top-right corner: Command 2 (“Backup”)",
            "Bottom-left corner: Show All Windows",
            "Bottom-right corner: Action Center",
            "Custom commands: on",
            "Modifier key: Ctrl",
            "Tray icon: shown",
            "Command 1 runs: program “notepad.exe”",
            "Command 2 (“Backup”) runs: program “robocopy” with arguments “C:\\a D:\\b” (hidden window)"
        ], lines);
    }

    [Fact]
    public void ArgumentsAreValidated()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsSeal.Capture(null!));
        Assert.Throws<ArgumentNullException>(() => SettingsSeal.Check(null!, "x"));
    }

    private const string Hostile = "a Z:\\/\"'(){}u-><.\u201C\u201D\n\r\0\u0085\u2028\u2029\u202E\u2066\u200B\uFEFF\uD800\uDC00\u00E9";

    private static string RandomText(Random random, int maxLength) =>
        new(Enumerable.Range(0, random.Next(0, maxLength + 1)).Select(_ => Hostile[random.Next(Hostile.Length)]).ToArray());

    private static CustomCommandSettings RandomCommand(Random random) => new()
    {
        Name = RandomText(random, 12),
        Command = RandomText(random, 16),
        Parameters = RandomText(random, 16),
        LaunchHidden = random.Next(2) == 0
    };

    private static string ExpectedRun(CustomCommandSettings command) =>
        command.Command.Length == 0 && command.Parameters.Length == 0 ? "nothing"
        : command.Parameters.Length == 0 ? "program {}" : "program {} with arguments {}";

    private static IEnumerable<string> RunValues(CustomCommandSettings command) =>
        command.Parameters.Length == 0
            ? (command.Command.Length == 0 ? [] : [command.Command])
            : [command.Command, command.Parameters];

    /// <summary>
    /// Property (finding b): for random and hostile names, programs and arguments, every approval line keeps its
    /// fixed shape outside the frames, and the frames read back as exactly the original field values.
    /// </summary>
    [Fact]
    public void ApprovalLines_RandomHostileValues_ParseBackToExactlyTheFieldValues()
    {
        var random = new Random(4242);
        for (var round = 0; round < 3000; round++)
        {
            var before = ApplicationSettings.CreateDefault();
            var after = ApplicationSettings.CreateDefault();
            before.CustomCommands[0] = RandomCommand(random);
            after.CustomCommands[0] = RandomCommand(random);
            after.EnableCustomCommands = true;
            after.TopLeftActionId = ActionCatalog.CustomId(0);

            var command = after.CustomCommands[0];
            var named = !string.IsNullOrWhiteSpace(command.Name);
            var slot = named ? "Command 1 ({})" : "Command 1";
            var summary = SettingsSeal.Summarize(after);
            Assert.All(summary, line => Assert.DoesNotContain(line, c => c is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029' or '\u202E'));

            var corner = summary[0];
            Assert.Equal("Top-left corner: " + slot, FramedValues.Skeleton(corner));
            Assert.Equal(named ? [command.Name] : [], FramedValues.Parse(corner));

            var runs = summary.Where(l => l.StartsWith("Command 1", StringComparison.Ordinal)).ToList();
            if (string.IsNullOrWhiteSpace(command.Command))
            {
                Assert.Empty(runs);
            }
            else
            {
                var line = Assert.Single(runs);
                Assert.Equal($"{slot} runs: {ExpectedRun(command)}{(command.LaunchHidden ? " (hidden window)" : "")}", FramedValues.Skeleton(line));
                Assert.Equal((named ? [command.Name] : Array.Empty<string>()).Concat(RunValues(command)), FramedValues.Parse(line));
            }

            var differences = SettingsSeal.Check(after, SettingsSeal.CreateApproval(before)).Differences;
            var was = before.CustomCommands[0];
            var nameLine = differences.SingleOrDefault(l => l.StartsWith("Command 1 name: ", StringComparison.Ordinal));
            Assert.Equal(was.Name != command.Name, nameLine is not null);
            if (nameLine is not null)
            {
                Assert.Equal("Command 1 name: {} -> {}", FramedValues.Skeleton(nameLine));
                Assert.Equal([was.Name, command.Name], FramedValues.Parse(nameLine));
            }

            var runLine = differences.SingleOrDefault(l => l.StartsWith("Command 1 now runs: ", StringComparison.Ordinal));
            Assert.Equal(was.Command != command.Command || was.Parameters != command.Parameters, runLine is not null);
            if (runLine is not null)
            {
                Assert.Equal($"Command 1 now runs: {ExpectedRun(command)} (was: {ExpectedRun(was)})", FramedValues.Skeleton(runLine));
                Assert.Equal(RunValues(command).Concat(RunValues(was)), FramedValues.Parse(runLine));
            }
        }
    }

    /// <summary>
    /// Property (finding g): the approval encoding is injective. Random strings, including unpaired surrogates and
    /// U+FFFD, always round-trip as Unchanged, and any change to any string field is Changed.
    /// </summary>
    [Fact]
    public void Approval_RandomStrings_RoundTripAndAnyChangeIsDetected()
    {
        var random = new Random(99);
        const string alphabet = "a\"\\\uD800\uDBFF\uDC00\uDFFF\uFFFD\u0000\u007F\u00E9/u";
        string Text() => new(Enumerable.Range(0, random.Next(0, 6)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
        for (var round = 0; round < 5000; round++)
        {
            var settings = ApplicationSettings.CreateDefault();
            var slot = random.Next(ApplicationSettings.CustomCommandCount);
            settings.CustomCommands[slot] = new CustomCommandSettings { Name = Text(), Command = Text(), Parameters = Text() };
            var approval = SettingsSeal.CreateApproval(settings);
            Assert.All(approval, c => Assert.InRange(c, ' ', '~'));
            Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(settings, approval).Status);

            var other = settings.Clone();
            var replacement = Text();
            var field = random.Next(3);
            var original = field switch { 0 => other.CustomCommands[slot].Name, 1 => other.CustomCommands[slot].Command, _ => other.CustomCommands[slot].Parameters };
            _ = field switch
            {
                0 => other.CustomCommands[slot].Name = replacement,
                1 => other.CustomCommands[slot].Command = replacement,
                _ => other.CustomCommands[slot].Parameters = replacement
            };
            var expected = string.Equals(original, replacement, StringComparison.Ordinal) ? SealStatus.Unchanged : SealStatus.Changed;
            Assert.Equal(expected, SettingsSeal.Check(other, approval).Status);
            Assert.Equal(expected == SealStatus.Unchanged, SettingsSeal.CreateApproval(other) == approval);
        }
    }

    /// <summary>
    /// Fuzz (finding f): no stored text makes Check throw. Mutations of a valid approval (inserted, deleted and
    /// replaced characters, including raw and escaped unpaired surrogates) are Unreadable unless the result is still
    /// exactly an approval the writer could produce.
    /// </summary>
    [Fact]
    public void Check_HostileStoredApprovals_NeverThrowAndAreUnreadableUnlessCanonical()
    {
        var random = new Random(31337);
        var settings = Configured();
        var valid = SettingsSeal.CreateApproval(settings);
        string[] pieces = ["\uD800", "\uDC00", "\\ud800", "\\uD800", "\\uDC00\\uD800", "\"", "\\", "{", "}", "[", "]", ",", ":", "1", "true", "null",
            "\"name\":\"\"", "\"hidden\":true", " ", "\u00E9", "\0", "\\u0022", "\\n", "-", "e5", "custom-1"];
        var statuses = new Dictionary<SealStatus, int>();
        for (var round = 0; round < 20_000; round++)
        {
            var text = valid;
            for (var edits = random.Next(1, 4); edits > 0; edits--)
            {
                var at = random.Next(text.Length + 1);
                text = random.Next(3) switch
                {
                    0 => text.Insert(at, pieces[random.Next(pieces.Length)]),
                    1 when at < text.Length => text.Remove(at, Math.Min(random.Next(1, 4), text.Length - at)),
                    _ => text[..at] + pieces[random.Next(pieces.Length)] + text[Math.Min(text.Length, at + 1)..]
                };
            }

            var check = SettingsSeal.Check(settings, text);
            statuses[check.Status] = statuses.GetValueOrDefault(check.Status) + 1;
            if (check.Status is SealStatus.Unchanged or SealStatus.Changed)
            {
                // Accepted only when the text is exactly what the writer produces for some settings.
                Assert.All(text, c => Assert.InRange(c, ' ', '~'));
                Assert.DoesNotContain(", ", text, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(SealStatus.Unreadable, check.Status);
                Assert.Equal([SettingsSeal.UnreadableDifference], check.Differences);
            }
        }

        Assert.True(statuses.GetValueOrDefault(SealStatus.Unreadable) > 15_000);
    }

    [Fact]
    public void Summarize_ShowsTheTrayIconSwitch()
    {
        var settings = Configured();
        Assert.Contains("Tray icon: shown", SettingsSeal.Summarize(settings));
        settings.AlwaysHideTrayIcon = true;
        Assert.Contains("Tray icon: hidden", SettingsSeal.Summarize(settings));
    }
}
