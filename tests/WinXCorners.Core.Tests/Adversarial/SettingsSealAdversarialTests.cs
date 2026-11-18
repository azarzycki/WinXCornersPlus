using System.Text;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;
using WinXCorners.Core.Tests.Security;

namespace WinXCorners.Core.Tests.Adversarial;

public class SettingsSealAdversarialTests
{
    private static ApplicationSettings WithCommand(string command, string parameters = "", string name = "")
    {
        var s = ApplicationSettings.CreateDefault();
        s.EnableCustomCommands = true;
        s.TopLeftActionId = ActionCatalog.CustomId(0);
        s.CustomCommands[0] = new CustomCommandSettings { Name = name, Command = command, Parameters = parameters };
        return s;
    }

    // Design point 2: "the user is shown exactly what will run". Two settings that run different things must not
    // produce a difference line whose "now runs" and "was" halves read identically.
    [Fact]
    public void Describe_CommandParameterSplitIsVisible()
    {
        var approved = WithCommand(@"C:\Program Files\Tool\tool.exe");
        var approval = SettingsSeal.CreateApproval(approved);
        var tampered = WithCommand(@"C:\Program", @"Files\Tool\tool.exe");

        var check = SettingsSeal.Check(tampered, approval);
        Assert.Equal(SealStatus.Changed, check.Status);
        var line = Assert.Single(check.Differences);
        const string marker = " (was: ";
        var nowPart = line[..line.IndexOf(marker, StringComparison.Ordinal)];
        var wasPart = line[(line.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..^1];
        Assert.NotEqual(nowPart["Command 1 now runs: ".Length..], wasPart);
    }

    [Fact]
    public void Summarize_CommandParameterSplitIsVisible()
    {
        var a = SettingsSeal.Summarize(WithCommand(@"C:\Program Files\Tool\tool.exe"));
        var b = SettingsSeal.Summarize(WithCommand(@"C:\Program", @"Files\Tool\tool.exe"));
        Assert.NotEqual(a, b);
    }

    // A changed command must be shown in full: truncation lets the dangerous tail hide behind the ellipsis.
    [Fact]
    public void Describe_ShowsTheWholeCommandNeverTruncated()
    {
        var approval = SettingsSeal.CreateApproval(WithCommand(@"C:\Windows\System32\cmd.exe", "/c echo hello"));
        var tampered = WithCommand(@"C:\Windows\System32\cmd.exe",
            "/c echo " + new string('h', 150) + @" & C:\Users\alex\AppData\Local\Temp\payload.exe");
        var check = SettingsSeal.Check(tampered, approval);
        Assert.Contains(check.Differences, l => l.Contains("payload.exe", StringComparison.Ordinal));
    }

    // U+2028/U+2029 are line/paragraph separators (categories Zl/Zp), neither Cc nor Cf, so DisplayText keeps them.
    [Theory]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void SummarizeAndDescribe_EscapeUnicodeLineSeparators(string separator)
    {
        var settings = WithCommand("notepad.exe" + separator + "Top-left corner: File Explorer", name: "n" + separator + "x");
        var lines = SettingsSeal.Summarize(settings).Concat(
            SettingsSeal.Check(settings, SettingsSeal.CreateApproval(ApplicationSettings.CreateDefault())).Differences);
        Assert.All(lines, l => Assert.DoesNotContain(separator, l, StringComparison.Ordinal));
    }

    // A user-chosen name is shown framed; a quote (ASCII or the frame's own closing mark) in the name cannot close the
    // frame early and let the name impersonate the rest of the line. The name's text is still shown, in full, but
    // only inside its frame. (Reviewer's version asserted the substring never appears at all, which no faithful
    // rendering of this name can satisfy; the line now has to read back as exactly slot + framed name.)
    [Theory]
    [InlineData("x\") runs: C:\\Windows\\notepad.exe")]
    [InlineData("x\u201D) runs: C:\\Windows\\notepad.exe")]
    [InlineData("x\\u{201D}) runs: notepad.exe")]
    public void Summarize_QuoteInNameCannotCloseItsFrame(string name)
    {
        var settings = WithCommand(@"C:\Users\alex\evil.exe", name: name);
        Assert.True(settings.CustomCommands[0].Name.Length <= WinXCorners.Core.Settings.SettingsSanitizer.MaxNameLength);
        var lines = SettingsSeal.Summarize(settings);
        var corner = Assert.Single(lines, l => l.StartsWith("Top-left corner: ", StringComparison.Ordinal));
        Assert.StartsWith("Top-left corner: Command 1 (\u201C", corner, StringComparison.Ordinal);
        Assert.EndsWith("\u201D)", corner, StringComparison.Ordinal);
        Assert.Equal([name], FramedValues.Parse(corner));
        var runs = Assert.Single(lines, l => l.Contains(" runs: ", StringComparison.Ordinal) && l.StartsWith("Command 1", StringComparison.Ordinal));
        Assert.Equal([name, @"C:\Users\alex\evil.exe"], FramedValues.Parse(runs));
        Assert.EndsWith(" runs: program \u201CC:\\Users\\alex\\evil.exe\u201D", runs, StringComparison.Ordinal);
    }

    // A stored approval must never crash Check (design: unreadable -> ask again, fail closed).
    [Fact]
    public void Check_EscapedLoneSurrogateInStoredApprovalIsNotUnchanged()
    {
        var valid = SettingsSeal.CreateApproval(ApplicationSettings.CreateDefault());
        var stored = valid.Replace("\"name\":\"\"", "\"name\":\"\\ud800\"", StringComparison.Ordinal);
        Assert.NotEqual(valid, stored);
        var check = SettingsSeal.Check(ApplicationSettings.CreateDefault(), stored);
        Assert.NotEqual(SealStatus.Unchanged, check.Status);
    }

    [Fact]
    public void Check_RawLoneSurrogateInStoredApprovalIsUnreadable()
    {
        var check = SettingsSeal.Check(ApplicationSettings.CreateDefault(), "{\"format\":\"\uD800\"}");
        Assert.Equal(SealStatus.Unreadable, check.Status);
    }

    // Utf8JsonWriter silently replaces a lone surrogate with U+FFFD, so the stored approval is not what was approved.
    // (settings.json itself rejects "\ud800", so this needs a lone surrogate typed or pasted into the Settings UI.)
    [Fact]
    public void Approval_LoneSurrogateRoundTripsAsUnchanged()
    {
        var settings = WithCommand("calc\uD800.exe");
        var approval = SettingsSeal.CreateApproval(settings);
        Assert.Equal(SealStatus.Unchanged, SettingsSeal.Check(settings, approval).Status);
    }

    [Fact]
    public void Approval_LoneSurrogateNeverCollidesWithReplacementCharacter()
    {
        var approval = SettingsSeal.CreateApproval(WithCommand("calc\uD800.exe"));
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(WithCommand("calc\uFFFD.exe"), approval).Status);
    }

    // Mutation gap: TryDeserialize's GetArrayLength() != 4 check. Without it, 3 commands makes Describe index out of range.
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public void Check_ApprovalWithWrongNumberOfWellFormedCommandsIsUnreadable(int count)
    {
        var commands = string.Join(",", Enumerable.Repeat("{\"name\":\"\",\"command\":\"\",\"parameters\":\"\",\"hidden\":false}", count));
        var stored = "{\"format\":\"WinXCornersPlus.SettingsApproval\",\"version\":3,\"topLeft\":\"none\",\"topRight\":\"none\"," +
                     "\"bottomLeft\":\"none\",\"bottomRight\":\"none\",\"customCommands\":false,\"modifier\":0,\"hideTrayIcon\":false,\"alwaysAdmin\":false,\"commands\":[" + commands + "]}";
        var check = SettingsSeal.Check(new ApplicationSettings { TopLeftActionId = "none", TopRightActionId = "none", BottomLeftActionId = "none", BottomRightActionId = "none" }, stored);
        Assert.Equal(SealStatus.Unreadable, check.Status);
    }

    // Format version 3 added "alwaysAdmin" after "hideTrayIcon" (version 2); this hand-built approval follows it.
    [Fact]
    public void Check_StoredApprovalSanityCheckOfMyHandBuiltFormatIsUnchanged()
    {
        var commands = string.Join(",", Enumerable.Repeat("{\"name\":\"\",\"command\":\"\",\"parameters\":\"\",\"hidden\":false}", 4));
        var stored = "{\"format\":\"WinXCornersPlus.SettingsApproval\",\"version\":3,\"topLeft\":\"none\",\"topRight\":\"none\"," +
                     "\"bottomLeft\":\"none\",\"bottomRight\":\"none\",\"customCommands\":false,\"modifier\":0,\"hideTrayIcon\":false,\"alwaysAdmin\":false,\"commands\":[" + commands + "]}";
        var check = SettingsSeal.Check(new ApplicationSettings { TopLeftActionId = "none", TopRightActionId = "none", BottomLeftActionId = "none", BottomRightActionId = "none" }, stored);
        Assert.Equal(SealStatus.Unchanged, check.Status);
    }

    [Fact]
    public void Check_DeeplyNestedOrHugeStoredApprovalIsUnreadable()
    {
        Assert.Equal(SealStatus.Unreadable, SettingsSeal.Check(ApplicationSettings.CreateDefault(), new string('[', 100_000)).Status);
        var huge = SettingsSeal.CreateApproval(ApplicationSettings.CreateDefault()).Replace("\"name\":\"\"", "\"name\":\"" + new string('a', 5_000_000) + "\"", StringComparison.Ordinal);
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(ApplicationSettings.CreateDefault(), huge).Status);
    }

    // The strict reader should not accept an approval the writer can never produce.
    [Fact]
    public void Check_DuplicateKeysInStoredApprovalAreUnreadable()
    {
        var valid = SettingsSeal.CreateApproval(ApplicationSettings.CreateDefault());
        var stored = valid.Replace("\"customCommands\":false", "\"customCommands\":true,\"customCommands\":false", StringComparison.Ordinal);
        Assert.Equal(SealStatus.Unreadable, SettingsSeal.Check(ApplicationSettings.CreateDefault(), stored).Status);
    }

    [Fact]
    public void Check_UnknownIdInStoredApprovalNeverComparesUnchangedWithNone()
    {
        var settings = ApplicationSettings.CreateDefault();
        settings.TopLeftActionId = ActionIds.None;
        var stored = SettingsSeal.CreateApproval(settings).Replace("\"topLeft\":\"none\"", "\"topLeft\":\"custom-9\"", StringComparison.Ordinal);
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(settings, stored).Status);
    }

    [Fact]
    public void Check_UnicodeLookAlikesAndCaseAreDifferences()
    {
        var approval = SettingsSeal.CreateApproval(WithCommand("notepad.exe"));
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(WithCommand("NOTEPAD.exe"), approval).Status);
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(WithCommand("n\u043Etepad.exe"), approval).Status); // Cyrillic o
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(WithCommand("note\u0301pad.exe".Normalize(NormalizationForm.FormC)), approval).Status);
        Assert.Equal(SealStatus.Changed, SettingsSeal.Check(WithCommand("notepad.exe", name: "x"), approval).Status);
    }

    [Fact]
    public void Summarize_LookAlikeSlotNameCannotPassAsAnotherSlot()
    {
        var s = ApplicationSettings.CreateDefault();
        s.EnableCustomCommands = true;
        s.TopLeftActionId = ActionCatalog.CustomId(1);
        s.CustomCommands[1] = new CustomCommandSettings { Name = "C\u043Ammand 1", Command = "evil.exe" };
        var lines = SettingsSeal.Summarize(s);
        Assert.Contains("Top-left corner: Command 2 (\u201CC\u043Ammand 1\u201D)", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Command 1", StringComparison.Ordinal));
    }

    [Fact]
    public void Check_RunsAgainstSanitizedAndUnsanitizedFormsConsistently()
    {
        var raw = WithCommand("notepad.exe", name: "  spaced  ");
        var approval = SettingsSeal.CreateApproval(raw);
        var sanitized = raw.Clone();
        WinXCorners.Core.Settings.SettingsSanitizer.Sanitize(sanitized);
        // Sanitizing trims the name, which is a visible difference, never a silent "Unchanged" of something else.
        var check = SettingsSeal.Check(sanitized, approval);
        Assert.Equal(SealStatus.Changed, check.Status);
        Assert.All(check.Differences, l => Assert.StartsWith("Command 1 name:", l, StringComparison.Ordinal));
    }
}
