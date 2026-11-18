using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class InstallLocationAssessorTests
{
    private static readonly string[] Roots = [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"];

    /// <summary>
    /// Without an ACL probe a local folder is never called protected, whatever its name: C:\Windows holds folders any
    /// user can write to. Network and relative folders are user-writable; a missing folder is unknown.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Program Files\WinXCornersPlus", InstallLocationKind.Unknown)]
    [InlineData(@"c:/program files (x86)/app/", InstallLocationKind.Unknown)]
    [InlineData(@"C:\Windows\Apps", InstallLocationKind.Unknown)]
    [InlineData(@"C:\Windows\Temp\WinXCornersPlus", InstallLocationKind.Unknown)]
    [InlineData(@"C:\Program FilesX\App", InstallLocationKind.Unknown)]
    [InlineData(@"C:\Program Files\..\Users\alex\App", InstallLocationKind.Unknown)]
    [InlineData(@"C:\Users\alex\Downloads", InstallLocationKind.Unknown)]
    [InlineData(@"D:\Tools", InstallLocationKind.Unknown)]
    [InlineData(@"\\server\share\App", InstallLocationKind.UserWritable)]
    [InlineData(@"App", InstallLocationKind.UserWritable)]
    [InlineData(@"\Program Files\App", InstallLocationKind.UserWritable)]
    [InlineData("", InstallLocationKind.Unknown)]
    [InlineData("  ", InstallLocationKind.Unknown)]
    [InlineData(null, InstallLocationKind.Unknown)]
    public void Assess_WithoutProbe_IsNeverProtected(string? directory, InstallLocationKind expected)
    {
        var result = InstallLocationAssessor.Assess(directory, Roots);
        Assert.Equal(expected, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
        Assert.DoesNotContain('!', result.Explanation);
    }

    [Fact]
    public void Assess_WithoutProbe_ExplainsThatPermissionsCouldNotBeReadAndWhenItMerelyLooksProtected()
    {
        var looksProtected = InstallLocationAssessor.Assess(@"C:\Program Files\App", Roots);
        Assert.Equal(InstallLocationKind.Unknown, looksProtected.Kind);
        Assert.Contains("could not be read", looksProtected.Explanation, StringComparison.Ordinal);
        Assert.Contains("normally admin-only", looksProtected.Explanation, StringComparison.Ordinal);

        var elsewhere = InstallLocationAssessor.Assess(@"D:\Tools", Roots);
        Assert.Contains("could not be read", elsewhere.Explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("admin-only", elsewhere.Explanation, StringComparison.Ordinal);
    }

    /// <summary>Protected only when the probe says non-administrators cannot write AND the folder is a local full path.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\App", true, InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Windows\Temp\WinXCornersPlus", true, InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Tools\App", false, InstallLocationKind.Protected)]
    [InlineData(@"\\?\C:\Program Files\App", false, InstallLocationKind.Protected)]
    [InlineData(@"\\server\share\App", false, InstallLocationKind.UserWritable)]
    [InlineData(@"\\?\UNC\srv\share\App", false, InstallLocationKind.UserWritable)]
    [InlineData(@"App", false, InstallLocationKind.UserWritable)]
    [InlineData(@"C:Program Files\App", false, InstallLocationKind.UserWritable)]
    [InlineData(null, true, InstallLocationKind.UserWritable)]
    [InlineData(null, false, InstallLocationKind.Unknown)]
    public void Assess_ProbeDecidesForLocalFoldersOnly(string? directory, bool writable, InstallLocationKind expected)
    {
        var result = InstallLocationAssessor.Assess(directory, Roots, writable);
        Assert.Equal(expected, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public void Assess_IgnoresBlankOrRelativeRootsSoTheyCannotProtectEverything()
    {
        var result = InstallLocationAssessor.Assess(@"C:\Users\alex\App", ["", "  ", @"\", "Users", null]);
        Assert.Equal(InstallLocationKind.Unknown, result.Kind);
        Assert.DoesNotContain("admin-only", result.Explanation, StringComparison.Ordinal);
        Assert.Equal(InstallLocationKind.Unknown, InstallLocationAssessor.Assess(@"C:\x", null).Kind);
    }

    [Fact]
    public void Assess_ReportsNormalizedDirectoryAndNamesItInTheExplanation()
    {
        var result = InstallLocationAssessor.Assess(@"""c:/program files/App/""", Roots);
        Assert.Equal(@"C:\program files\App", result.Directory);
        Assert.Contains(@"C:\program files\App", result.Explanation);
    }

    [Fact]
    public void Assess_EscapesControlCharactersInExplanation()
    {
        var result = InstallLocationAssessor.Assess("C:\\Evil\nOnly administrators can change this.", Roots);
        Assert.DoesNotContain('\n', result.Explanation);
    }
}
