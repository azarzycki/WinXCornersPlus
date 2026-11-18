using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Adversarial;

public class LaunchAndPathAdversarialTests
{
    private static readonly SystemDirectories Dirs = new(
        @"C:\Windows", @"C:\Windows\System32", @"C:\Users\alex",
        [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"]);

    private static readonly string[] Roots = [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"];

    // "Plain https" links: userinfo lets the visible start of the URL name a trusted host while another host loads.
    [Theory]
    [InlineData("https://github.com@evil.example/")]
    [InlineData("https://github.com:443@evil.example/")]
    [InlineData("https://user:pass@github.com/")]
    [InlineData(@"https://evil.example\@github.com/")]
    public void Link_RejectsUserInfo(string url)
    {
        Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(url, "", false, LaunchTargetKind.Link), ElevationMode.ElevatedWithStandardShell, Dirs, out _, out _));
        Assert.Throws<ArgumentException>(() => SystemTargets.Link(url));
    }

    [Theory]
    [InlineData(@"https:\\example.com")]
    [InlineData("https:/example.com")]
    [InlineData("javascript:https://example.com")]
    [InlineData("file://https://example.com")]
    [InlineData(" http://example.com")]
    [InlineData("https://exa\u0000mple.com")]
    [InlineData("https://example.com/\nx")]
    [InlineData("https://example.com/x\u0085y")]
    [InlineData("\"https://example.com\" --evil")]
    [InlineData("https://")]
    public void Link_RejectsNonPlainHttps(string url)
    {
        Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(url, "", false, LaunchTargetKind.Link), ElevationMode.ElevatedWithStandardShell, Dirs, out _, out _));
    }

    [Theory]
    [InlineData("HTTPS://example.com/releases")]
    [InlineData("  https://example.com  ")]
    public void Link_AcceptsMixedCaseAndTrimsAndIsDeElevated(string url)
    {
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest(url, "", true, LaunchTargetKind.Link), ElevationMode.ElevatedWithStandardShell, Dirs, out var plan, out _));
        Assert.Equal(LaunchIntegrity.DeElevated, plan.Integrity);
        Assert.False(plan.Hidden);
        Assert.Equal(@"C:\Users\alex", plan.WorkingDirectory);
    }

    // What is validated must be what runs. With the \\?\ prefix Windows does not resolve "..", yet the planner
    // validates the lexically resolved form and hands the unresolved one to ShellExecute.
    [Theory]
    [InlineData(@"\\?\C:\Users\alex\Downloads\..\..\..\Windows\explorer.exe")]
    [InlineData(@"\\?\C:\Windows\explorer.exe")]
    [InlineData(@"\\.\C:\Windows\explorer.exe")]
    [InlineData(@"//?/C:/Windows/explorer.exe")]
    [InlineData(@"\\./C:\Windows\explorer.exe")]
    [InlineData(@"\??\C:\Windows\explorer.exe")]
    public void BuiltIn_LaunchesTheNormalizedPathItValidatedAndRejectsDevicePaths(string target)
    {
        Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(target, "", false, LaunchTargetKind.BuiltIn), ElevationMode.ElevatedWithStandardShell, Dirs, out _, out var error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\..\explorer.exe", @"C:\Windows\explorer.exe")]
    [InlineData(@"c:/windows/explorer.exe", @"C:\windows\explorer.exe")]
    [InlineData(@"""C:\Windows\.\System32\taskmgr.exe""", @"C:\Windows\System32\taskmgr.exe")]
    public void BuiltIn_FileNameIsTheNormalizedPathThatWasValidated(string target, string expected)
    {
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest(target, "", false, LaunchTargetKind.BuiltIn), ElevationMode.ElevatedWithStandardShell, Dirs, out var plan, out _));
        Assert.Equal(expected, plan.FileName);
        Assert.Equal(WindowsPath.Normalize(plan.FileName), plan.FileName);
    }

    [Fact]
    public void BuiltIn_UriTargetAndCustomCommandsAreLaunchedAsGiven()
    {
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest("ms-settings:display", "", false, LaunchTargetKind.BuiltIn), ElevationMode.ElevatedWithStandardShell, Dirs, out var uri, out _));
        Assert.Equal("ms-settings:display", uri.FileName);
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest(@"C:\Tools\..\x.exe", "", false, LaunchTargetKind.CustomCommand), ElevationMode.ElevatedWithStandardShell, Dirs, out var custom, out _));
        Assert.Equal(@"C:\Tools\..\x.exe", custom.FileName);
    }

    // Mutation gap: BuiltInUriSchemes uses StartsWith; nothing tests a scheme appearing later in the target.
    [Theory]
    [InlineData(@"C:\Users\alex\ms-settings:x.exe")]
    [InlineData(@"xms-settings:")]
    [InlineData(@" C:\Users\alex\evil.exe ms-settings:")]
    [InlineData(@"evil.exe")]
    [InlineData(@"C:Windows\explorer.exe")]
    [InlineData(@"\Windows\explorer.exe")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume3\Windows\explorer.exe")]
    [InlineData(@"C:\Windows \explorer.exe")]
    [InlineData(@"C:\WINDOW~1\explorer.exe")]
    [InlineData(@"C:\Windows/../Users/alex/x.exe")]
    [InlineData(@"\\localhost\C$\Windows\explorer.exe")]
    public void BuiltIn_RejectsTargetsThatAreNotAbsoluteSystemPaths(string target)
    {
        Assert.False(LaunchPlanner.TryPlan(new LaunchRequest(target, "", false, LaunchTargetKind.BuiltIn), ElevationMode.ElevatedWithStandardShell, Dirs, out _, out _));
    }

    [Theory]
    [InlineData(LaunchTargetKind.BuiltIn)]
    [InlineData(LaunchTargetKind.CustomCommand)]
    [InlineData(LaunchTargetKind.Link)]
    public void Elevated_NeverSameAsApp_AndWorkingDirectoryIsASystemOrProfileFolder(LaunchTargetKind kind)
    {
        var target = kind switch
        {
            LaunchTargetKind.BuiltIn => @"C:\Windows\explorer.exe",
            LaunchTargetKind.Link => "https://example.com",
            _ => "notepad.exe"
        };
        Assert.True(LaunchPlanner.TryPlan(new LaunchRequest(target, "a", true, kind), ElevationMode.ElevatedWithStandardShell, Dirs, out var plan, out _));
        Assert.Equal(LaunchIntegrity.DeElevated, plan.Integrity);
        Assert.True(plan.WorkingDirectory is @"C:\Windows\System32" or @"C:\Users\alex", plan.WorkingDirectory);
    }

    // Per Microsoft's documented normalization order ("..", then trimming) these are ordinary names, as here;
    // a trailing-dot segment that Win32 would trim must fail closed (not be "under").
    [Theory]
    [InlineData(@"C:\Windows\System32\.. \..\..\Users\alex\evil.exe", true)]
    [InlineData(@"C:\Windows.\explorer.exe", false)]
    [InlineData(@"C:\Windows \explorer.exe", false)]
    public void IsUnder_DotAndSpaceSegments(string path, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsUnder(path, @"C:\Windows"));
    }

    [Theory]
    [InlineData(@"C:\Windows\..\Users\x", @"C:\Windows", false)]
    [InlineData(@"C:\Windows\..\..\..\Windows\x", @"C:\Windows", true)]
    [InlineData(@"\\?\C:\Windows\x", @"C:\Windows", true)]
    [InlineData(@"\\?\UNC\srv\share\x", @"\\srv\share", true)]
    [InlineData(@"\\srv\..\x", @"\\srv\share", false)]
    [InlineData(@"\\srv\share\..\..\other\x", @"\\srv\share", true)]
    [InlineData(@"\\srv\share\..\..\other\x", @"\\srv\other", false)]
    [InlineData(@"C:\Windows", @"C:\Windows\System32", false)]
    [InlineData(@"C:\Windowsx\a", @"C:\Windows", false)]
    [InlineData(@"C:", @"C:", false)]
    [InlineData(@"C:x", @"C:\", false)]
    [InlineData(@"\??\C:\Windows\x", @"C:\Windows", false)]
    [InlineData(@"C:\Windows:stream\x", @"C:\Windows", false)]
    public void IsUnder_EdgeCases(string path, string root, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsUnder(path, root));
    }

    // Seeded property check: IsUnder implies the normalized path starts with the normalized root plus a separator.
    [Fact]
    public void IsUnder_PropertyHoldsForRandomPaths()
    {
        var random = new Random(1234);
        string[] parts = ["Windows", "System32", "..", ".", "Users", "x", "", "WINDOWS", "Program Files", ".. ", "a.", "/", "\\"];
        for (var i = 0; i < 20000; i++)
        {
            var path = (random.Next(3) == 0 ? @"\\?\" : "") + "C:" + string.Concat(Enumerable.Range(0, random.Next(1, 8)).Select(_ => "\\" + parts[random.Next(parts.Length)]));
            if (WindowsPath.IsUnder(path, @"C:\Windows"))
            {
                var n = WindowsPath.Normalize(path);
                Assert.True(n.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase) || n.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase), path + " -> " + n);
            }
        }
    }

    // C:\Windows contains folders any user can create files and folders in (Temp, Tasks, tracing,
    // System32\spool\drivers\color...). Without an ACL probe, those are reported as admin-only.
    [Theory]
    [InlineData(@"C:\Windows\Temp\WinXCornersPlus")]
    [InlineData(@"C:\Windows\Tasks\WinXCornersPlus")]
    [InlineData(@"C:\Windows\System32\spool\drivers\color\WinXCornersPlus")]
    public void Assess_FoldersUnderWindowsAreNotProtectedByName(string dir)
    {
        Assert.NotEqual(InstallLocationKind.Protected, InstallLocationAssessor.Assess(dir, Roots).Kind);
    }

    // A local ACL probe says nothing about who can write the share on the remote server.
    [Fact]
    public void Assess_NetworkFolderIsNeverProtected()
    {
        Assert.NotEqual(InstallLocationKind.Protected,
            InstallLocationAssessor.Assess(@"\\server\share\WinXCornersPlus", Roots, writableByNonAdministrators: false).Kind);
    }

    // Fail closed: no folder is Protected without a probe saying so; a probe saying "not writable" protects only a
    // local, fully qualified folder. (Reviewer's version expected Protected for \\?\C:\Program Files\App with no
    // probe at all, i.e. by folder name; that is exactly the rule finding j replaced.)
    [Theory]
    [InlineData(@"\\?\C:\Program Files\App", false, InstallLocationKind.Protected)]
    [InlineData(@"\\?\C:\Program Files\App", null, InstallLocationKind.Unknown)]
    [InlineData(@"C:\PROGRA~1\App", null, InstallLocationKind.Unknown)]
    [InlineData(@"C:\Program Files\App\..\..\Users\x", null, InstallLocationKind.Unknown)]
    [InlineData(@"\\?\UNC\srv\share\App", false, InstallLocationKind.UserWritable)]
    [InlineData(@"\\?\UNC\srv\share\App", null, InstallLocationKind.UserWritable)]
    [InlineData(@"C:Program Files\App", false, InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Program Files .\App", null, InstallLocationKind.Unknown)]
    public void Assess_EdgeCasesFailClosed(string dir, bool? writable, InstallLocationKind expected)
    {
        Assert.Equal(expected, InstallLocationAssessor.Assess(dir, Roots, writable).Kind);
    }

    [Theory]
    [InlineData(true, true, false, SealStatus.Unchanged, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, true, SealStatus.Unchanged, AutoElevateAction.DoNotElevate)]
    [InlineData(false, false, false, SealStatus.Unchanged, AutoElevateAction.DoNotElevate)]
    [InlineData(true, false, false, SealStatus.NeverApproved, AutoElevateAction.AskBeforeElevating)]
    [InlineData(true, false, false, SealStatus.Changed, AutoElevateAction.AskBeforeElevating)]
    [InlineData(true, false, false, SealStatus.Unreadable, AutoElevateAction.AskBeforeElevating)]
    [InlineData(true, false, false, SealStatus.Unchanged, AutoElevateAction.Elevate)]
    public void AutoElevate_OnlyElevatesSilentlyWhenTheApprovalMatchesExactly(bool always, bool elevated, bool noElevate, SealStatus approval, AutoElevateAction expected)
    {
        Assert.Equal(expected, AutoElevatePolicy.Decide(always, elevated, noElevate, approval));
    }

    [Fact]
    public void ElevatedStart_ProceedsOnlyForUnchanged()
    {
        foreach (var status in Enum.GetValues<SealStatus>().Append((SealStatus)(-1)))
        {
            var action = ElevatedStartPolicy.Decide(true, new SealCheck(status, []));
            Assert.Equal(status == SealStatus.Unchanged, action == ElevatedStartAction.Proceed);
        }
    }
}
