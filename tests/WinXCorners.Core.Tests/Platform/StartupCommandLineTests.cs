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
}
