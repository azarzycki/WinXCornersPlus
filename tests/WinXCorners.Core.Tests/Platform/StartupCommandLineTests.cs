using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Tests.Platform;

public class StartupCommandLineTests
{
    private const string Exe = @"C:\Program Files\WinXCornersPlus\WinXCornersPlus.exe";

    [Theory]
    [InlineData(Exe, "\"" + Exe + "\"")]
    [InlineData(@"c:/apps//x.exe", @"""C:\apps\x.exe""")]
    [InlineData("\"" + Exe + "\"", "\"" + Exe + "\"")]
    public void Build_AlwaysQuotesNormalizedPath(string input, string expected)
    {
        Assert.Equal(expected, StartupCommandLine.Build(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x.exe")]
    [InlineData(@"\apps\x.exe")]
    [InlineData("C:\\a\"b.exe")]
    [InlineData("C:\\a\nb.exe")]
    public void Build_RejectsUnusablePaths(string input)
    {
        Assert.Throws<ArgumentException>(() => StartupCommandLine.Build(input));
    }

    [Fact]
    public void Build_RoundTripsThroughTryGetExecutablePath()
    {
        Assert.True(StartupCommandLine.TryGetExecutablePath(StartupCommandLine.Build(Exe), out var path));
        Assert.Equal(Exe, path);
        Assert.True(StartupCommandLine.PointsTo(StartupCommandLine.Build(Exe), Exe));
    }

    [Theory]
    [InlineData("\"" + Exe + "\"", Exe)]
    [InlineData("  \"" + Exe + "\"  --no-elevate", Exe)]
    [InlineData(Exe, Exe)]
    [InlineData(Exe + " --show-settings", Exe)]
    [InlineData(@"C:\Program Files\App\run.EXE --x y.exe", @"C:\Program Files\App\run.EXE")]
    [InlineData(@"C:\Tools\app.exe.bak\real.exe", @"C:\Tools\app.exe.bak\real.exe")]
    [InlineData(@"C:\Tools\launcher.cmd /quiet", @"C:\Tools\launcher.cmd")]
    [InlineData(@"C:\Tools\launcher", @"C:\Tools\launcher")]
    public void TryGetExecutablePath_ExtractsProgram(string value, string expected)
    {
        Assert.True(StartupCommandLine.TryGetExecutablePath(value, out var path));
        Assert.Equal(expected, path);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\unterminated.exe")]
    [InlineData("\"\" --arg")]
    [InlineData("\"  \"")]
    [InlineData("\"C:\\a.exe\"glued")]
    public void TryGetExecutablePath_RejectsMalformedValues(string? value)
    {
        Assert.False(StartupCommandLine.TryGetExecutablePath(value, out var path));
        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData("\"" + Exe + "\"", Exe, true)]
    [InlineData("\"c:/program files/winxcornersplus/WINXCORNERSPLUS.EXE\" --no-elevate", Exe, true)]
    [InlineData("\"C:\\Users\\x\\Downloads\\WinXCornersPlus.exe\"", Exe, false)]
    [InlineData("\"C:\\Program Files\\WinXCornersPlus\\WinXCornersPlus.exe.old\"", Exe, false)]
    [InlineData(null, Exe, false)]
    [InlineData("\"" + Exe + "\"", "", false)]
    [InlineData("\"" + Exe + "\"", null, false)]
    public void PointsTo_DetectsStaleEntries(string? value, string? exe, bool expected)
    {
        Assert.Equal(expected, StartupCommandLine.PointsTo(value, exe));
    }

    /// <summary>
    /// An unquoted value whose program path contains spaces is never "this executable": Windows may run
    /// C:\Program.exe for it. It is reported as stale so the user can save it again (quoted).
    /// </summary>
    [Theory]
    [InlineData(Exe, Exe)]
    [InlineData(Exe + " --no-elevate", Exe)]
    [InlineData("  " + Exe, Exe)]
    [InlineData(@"C:\Program Files\App\run.EXE --x y.exe", @"C:\Program Files\App\run.EXE")]
    public void An_unquoted_value_with_spaces_is_never_this_executable(string value, string exe)
    {
        Assert.Equal(RunEntryState.UnquotedWithSpaces, StartupCommandLine.Classify(value, exe));
        Assert.False(StartupCommandLine.PointsTo(value, exe));
    }

    [Theory]
    [InlineData(null, Exe, RunEntryState.None)]
    [InlineData("  ", Exe, RunEntryState.None)]
    [InlineData("\"C:\\unterminated.exe", Exe, RunEntryState.Malformed)]
    [InlineData("\"" + Exe + "\"", Exe, RunEntryState.ThisExecutable)]
    [InlineData("\"" + Exe + "\" --no-elevate", Exe, RunEntryState.ThisExecutable)]
    [InlineData(@"C:\Apps\WinXCornersPlus.exe", @"C:\Apps\WinXCornersPlus.exe", RunEntryState.ThisExecutable)]
    [InlineData(@"C:\Apps\WinXCornersPlus.exe --show-settings", @"c:\apps\winxcornersplus.EXE", RunEntryState.ThisExecutable)]
    [InlineData(@"C:\Apps\Other.exe", @"C:\Apps\WinXCornersPlus.exe", RunEntryState.OtherProgram)]
    [InlineData("\"C:\\Users\\x\\Downloads\\WinXCornersPlus.exe\"", Exe, RunEntryState.OtherProgram)]
    [InlineData("\"" + Exe + "\"", null, RunEntryState.OtherProgram)]
    [InlineData("\"" + Exe + "\"", "", RunEntryState.OtherProgram)]
    public void Classify_covers_every_state(string? value, string? exe, RunEntryState expected)
    {
        Assert.Equal(expected, StartupCommandLine.Classify(value, exe));
        Assert.Equal(expected == RunEntryState.ThisExecutable, StartupCommandLine.PointsTo(value, exe));
    }
}
