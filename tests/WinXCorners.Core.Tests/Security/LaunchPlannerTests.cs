using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class LaunchPlannerTests
{
    private static readonly SystemDirectories Dirs = TestData.Directories;

    public static TheoryData<LaunchTargetKind, bool> KindsAndElevation()
    {
        var data = new TheoryData<LaunchTargetKind, bool>();
        foreach (var kind in Enum.GetValues<LaunchTargetKind>())
        {
            data.Add(kind, false);
            data.Add(kind, true);
        }

        return data;
    }

    private static LaunchRequest ValidRequest(LaunchTargetKind kind, bool hidden = false) => kind switch
    {
        LaunchTargetKind.BuiltIn => SystemTargets.FileExplorer(Dirs) with { Hidden = hidden },
        LaunchTargetKind.Link => SystemTargets.Link("https://github.com/asteski/winxcornersplus/releases") with { Hidden = hidden },
        _ => new LaunchRequest("notepad.exe", "notes.txt", hidden, LaunchTargetKind.CustomCommand)
    };

    [Theory]
    [MemberData(nameof(KindsAndElevation))]
    public void TryPlan_SetsIntegrityAndWorkingDirectoryForEveryKind(LaunchTargetKind kind, bool elevated)
    {
        Assert.True(LaunchPlanner.TryPlan(ValidRequest(kind), (elevated ? ElevationMode.ElevatedWithStandardShell : ElevationMode.NotElevated), Dirs, out var plan, out var error), error);
        Assert.Null(error);
        Assert.Equal(elevated ? LaunchIntegrity.DeElevated : LaunchIntegrity.SameAsApp, plan.Integrity);
        Assert.Equal(kind == LaunchTargetKind.BuiltIn ? Dirs.System32 : Dirs.UserProfile, plan.WorkingDirectory);
        Assert.False(WindowsPath.IsUnder(plan.WorkingDirectory, @"C:\Program Files\WinXCornersPlus"));
    }

    [Fact]
    public void TryPlan_NoInputYieldsSameAsAppWhileElevated()
    {
        string[] targets = ["notepad.exe", @"C:\Windows\explorer.exe", "ms-settings:", "https://example.com", "", "\"x\"", "a\nb", @"C:\Users\x\evil.exe"];
        string?[] arguments = ["", null, "--x", "\t"];
        foreach (var kind in Enum.GetValues<LaunchTargetKind>().Append((LaunchTargetKind)99))
        foreach (var target in targets)
        foreach (var args in arguments)
        foreach (var hidden in new[] { false, true })
        {
            if (LaunchPlanner.TryPlan(new LaunchRequest(target, args!, hidden, kind), ElevationMode.ElevatedWithStandardShell, Dirs, out var plan, out _))
            {
                Assert.Equal(LaunchIntegrity.DeElevated, plan.Integrity);
            }
        }
    }

    [Theory]
    [InlineData(LaunchTargetKind.BuiltIn, false)]
    [InlineData(LaunchTargetKind.Link, false)]
    [InlineData(LaunchTargetKind.CustomCommand, true)]
    public void TryPlan_HonoursHiddenOnlyForCustomCommands(LaunchTargetKind kind, bool expectedHidden)
    {
        Assert.True(LaunchPlanner.TryPlan(ValidRequest(kind, hidden: true), ElevationMode.NotElevated, Dirs, out var plan, out _));
        Assert.Equal(expectedHidden, plan.Hidden);
    }

    [Theory]
    [InlineData("", "", LaunchTargetKind.CustomCommand)]
    [InlineData("   ", "", LaunchTargetKind.CustomCommand)]
    [InlineData("\"\"", "", LaunchTargetKind.CustomCommand)]
    [InlineData("note\npad.exe", "", LaunchTargetKind.CustomCommand)]
    [InlineData("notepad.exe", "a\rb", LaunchTargetKind.CustomCommand)]
    [InlineData("notepad.exe", "a\0b", LaunchTargetKind.CustomCommand)]
    [InlineData("notepad.exe", "a\tb", LaunchTargetKind.CustomCommand)]
    [InlineData("C:\\a\" --evil \"b.exe", "", LaunchTargetKind.CustomCommand)]
    [InlineData("http://example.com", "", LaunchTargetKind.Link)]
    [InlineData("file:///C:/Windows/System32/cmd.exe", "", LaunchTargetKind.Link)]
    [InlineData("example.com", "", LaunchTargetKind.Link)]
    [InlineData("https://", "", LaunchTargetKind.Link)]
    [InlineData("/https://example.com", "", LaunchTargetKind.Link)]
    [InlineData("explorer.exe", "", LaunchTargetKind.BuiltIn)]
    [InlineData(@"C:\Users\alex\explorer.exe", "", LaunchTargetKind.BuiltIn)]
    [InlineData(@"C:\Windows\..\Users\alex\explorer.exe", "", LaunchTargetKind.BuiltIn)]
    [InlineData(@"\\server\share\taskmgr.exe", "", LaunchTargetKind.BuiltIn)]
    [InlineData("notepad.exe", "", (LaunchTargetKind)42)]
    public void TryPlan_RejectsUnsafeRequests(string target, string arguments, LaunchTargetKind kind)
    {
        foreach (var elevated in new[] { false, true })
        {
            Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(target, arguments, false, kind), (elevated ? ElevationMode.ElevatedWithStandardShell : ElevationMode.NotElevated), Dirs, out var plan, out var error));
            Assert.Null(plan);
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public void TryPlan_RejectsRelativeSystemDirectoryFacts()
    {
        var bad = Dirs with { System32 = "System32", UserProfile = "" };
        Assert.False(LaunchPlanner.TryPlan(SystemTargets.FileExplorer(Dirs), ElevationMode.ElevatedWithStandardShell, bad, out _, out var builtInError));
        Assert.NotNull(builtInError);
        Assert.False(LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.CustomCommand), ElevationMode.ElevatedWithStandardShell, bad, out _, out var customError));
        Assert.NotNull(customError);
        var badWindows = Dirs with { Windows = "Windows", System32 = "System32" };
        Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(@"C:\Windows\explorer.exe", "", false, LaunchTargetKind.BuiltIn), ElevationMode.NotElevated, badWindows, out _, out _));
    }

    [Fact]
    public void TryPlan_StripsSurroundingQuotesAndWhitespace()
    {
        var request = new LaunchRequest("  \"C:\\Program Files\\Tool\\tool.exe\"  ", "  --flag  ", false, LaunchTargetKind.CustomCommand);
        Assert.True(LaunchPlanner.TryPlan(request, ElevationMode.NotElevated, Dirs, out var plan, out _));
        Assert.Equal(@"C:\Program Files\Tool\tool.exe", plan.FileName);
        Assert.Equal("--flag", plan.Arguments);
    }

    [Fact]
    public void TryPlan_TreatsNullArgumentsAsEmpty()
    {
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest("notepad.exe", null!, false, LaunchTargetKind.CustomCommand), ElevationMode.NotElevated, Dirs, out var plan, out _));
        Assert.Equal(string.Empty, plan.Arguments);
    }

    [Fact]
    public void TryPlan_ThrowsOnNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => LaunchPlanner.TryPlan(null!, ElevationMode.NotElevated, Dirs, out _, out _));
        Assert.Throws<ArgumentNullException>(() => LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.Link), ElevationMode.NotElevated, null!, out _, out _));
    }

    [Fact]
    public void SystemTargets_AreAbsoluteAndInsideTheSuppliedDirectories()
    {
        var dirs = new SystemDirectories(@"D:\WinNT", @"D:\WinNT\System32", @"D:\Users\bo", []);
        var explorer = SystemTargets.FileExplorer(dirs);
        var taskManager = SystemTargets.TaskManager(dirs);

        Assert.Equal(@"D:\WinNT\explorer.exe", explorer.Target);
        Assert.Equal(@"D:\WinNT\System32\taskmgr.exe", taskManager.Target);
        foreach (var request in new[] { explorer, taskManager })
        {
            Assert.Equal(LaunchTargetKind.BuiltIn, request.Kind);
            Assert.True(WindowsPath.IsFullyQualified(request.Target));
            Assert.True(WindowsPath.IsUnder(request.Target, dirs.Windows));
            Assert.True(LaunchPlanner.TryPlan(request, ElevationMode.ElevatedWithStandardShell, dirs, out var plan, out _));
            Assert.Equal(dirs.System32, plan.WorkingDirectory);
        }

        var settings = SystemTargets.SystemSettings();
        Assert.Equal("ms-settings:", settings.Target);
        Assert.True(LaunchPlanner.TryPlan(settings, ElevationMode.ElevatedWithStandardShell, dirs, out _, out _));
    }

    [Fact]
    public void SystemTargets_TaskManagerWorksWhenSystem32IsOutsideWindowsFolder()
    {
        var dirs = Dirs with { System32 = @"E:\Sys32" };
        Assert.True(LaunchPlanner.TryPlan(SystemTargets.TaskManager(dirs), ElevationMode.NotElevated, dirs, out var plan, out _));
        Assert.Equal(@"E:\Sys32\taskmgr.exe", plan.FileName);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData("releases")]
    public void SystemTargets_LinkRejectsNonHttps(string url)
    {
        Assert.Throws<ArgumentException>(() => SystemTargets.Link(url));
    }

    [Fact]
    public void SystemTargets_LinkAcceptsHttpsCaseInsensitively()
    {
        var link = SystemTargets.Link(" HTTPS://Example.com/x ");
        Assert.Equal("HTTPS://Example.com/x", link.Target);
        Assert.Equal(LaunchTargetKind.Link, link.Kind);
    }

    [Fact]
    public void SystemTargets_ThrowOnNullDirectories()
    {
        Assert.Throws<ArgumentNullException>(() => SystemTargets.FileExplorer(null!));
        Assert.Throws<ArgumentNullException>(() => SystemTargets.TaskManager(null!));
    }

    [Fact]
    public void Describe_IsOneLineWithTargetArgumentsAndFlags()
    {
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest("cmd.exe", "/c dir", true, LaunchTargetKind.CustomCommand), ElevationMode.ElevatedWithStandardShell, Dirs, out var plan, out _));
        Assert.Equal("cmd.exe /c dir (without administrator rights, hidden)", LaunchPlanner.Describe(plan));

        Assert.True(LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.BuiltIn), ElevationMode.NotElevated, Dirs, out var plain, out _));
        Assert.Equal(@"C:\Windows\explorer.exe", LaunchPlanner.Describe(plain));

        var forged = new LaunchPlan("a\nb", "c\rd", @"C:\", false, LaunchIntegrity.SameAsApp);
        var line = LaunchPlanner.Describe(forged);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal(string.Empty, LaunchPlanner.Describe(forged with { FileName = null!, Arguments = null! }));
        Assert.Throws<ArgumentNullException>(() => LaunchPlanner.Describe(null!));
    }

    [Theory]
    [InlineData(ElevationMode.NotElevated, LaunchIntegrity.SameAsApp)]
    [InlineData(ElevationMode.ElevatedWithStandardShell, LaunchIntegrity.DeElevated)]
    [InlineData(ElevationMode.ElevatedWithoutStandardRights, LaunchIntegrity.Administrator)]
    [InlineData((ElevationMode)42, LaunchIntegrity.DeElevated)]
    public void Integrity_follows_the_three_elevation_states(ElevationMode mode, LaunchIntegrity expected)
    {
        foreach (var kind in Enum.GetValues<LaunchTargetKind>())
        {
            Assert.True(LaunchPlanner.TryPlan(ValidRequest(kind), mode, Dirs, out var plan, out var error), error);
            Assert.Equal(expected, plan.Integrity);
        }
    }

    [Fact]
    public void Describe_says_when_a_launch_gets_administrator_rights()
    {
        Assert.True(LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.CustomCommand), ElevationMode.ElevatedWithoutStandardRights, Dirs, out var plan, out _));
        Assert.Contains("with administrator rights", LaunchPlanner.Describe(plan), StringComparison.Ordinal);
        Assert.True(LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.CustomCommand), ElevationMode.ElevatedWithStandardShell, Dirs, out plan, out _));
        Assert.Contains("without administrator rights", LaunchPlanner.Describe(plan), StringComparison.Ordinal);
        Assert.True(LaunchPlanner.TryPlan(ValidRequest(LaunchTargetKind.CustomCommand), ElevationMode.NotElevated, Dirs, out plan, out _));
        Assert.DoesNotContain("administrator", LaunchPlanner.Describe(plan), StringComparison.Ordinal);
    }
}
