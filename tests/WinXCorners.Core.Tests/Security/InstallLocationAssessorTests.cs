using WinXCorners.Core.Security;

namespace WinXCorners.Core.Tests.Security;

public class InstallLocationAssessorTests
{
    private static readonly string[] Roots = [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\Windows"];

    [Theory]
    [InlineData(@"C:\Program Files\WinXCornersPlus", InstallLocationKind.Protected)]
    [InlineData(@"c:/program files (x86)/app/", InstallLocationKind.Protected)]
    [InlineData(@"C:\Windows\Apps", InstallLocationKind.Protected)]
    [InlineData(@"C:\Program FilesX\App", InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Program Files\..\Users\alex\App", InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Users\alex\Downloads", InstallLocationKind.UserWritable)]
    [InlineData(@"D:\Tools", InstallLocationKind.UserWritable)]
    [InlineData(@"\\server\share\App", InstallLocationKind.UserWritable)]
    [InlineData(@"App", InstallLocationKind.UserWritable)]
    [InlineData(@"\Program Files\App", InstallLocationKind.UserWritable)]
    [InlineData("", InstallLocationKind.Unknown)]
    [InlineData("  ", InstallLocationKind.Unknown)]
    [InlineData(null, InstallLocationKind.Unknown)]
    public void Assess_UsesFolderHeuristicWithoutProbe(string? directory, InstallLocationKind expected)
    {
        var result = InstallLocationAssessor.Assess(directory, Roots);
        Assert.Equal(expected, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
        Assert.DoesNotContain('!', result.Explanation);
    }

    [Theory]
    [InlineData(@"C:\Program Files\App", true, InstallLocationKind.UserWritable)]
    [InlineData(@"C:\Tools\App", false, InstallLocationKind.Protected)]
    [InlineData(null, true, InstallLocationKind.UserWritable)]
    [InlineData(null, false, InstallLocationKind.Protected)]
    public void Assess_ProbeResultWins(string? directory, bool writable, InstallLocationKind expected)
    {
        var result = InstallLocationAssessor.Assess(directory, Roots, writable);
        Assert.Equal(expected, result.Kind);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public void Assess_IgnoresBlankOrRelativeRootsSoTheyCannotProtectEverything()
    {
        var result = InstallLocationAssessor.Assess(@"C:\Users\alex\App", ["", "  ", @"\", "Users", null]);
        Assert.Equal(InstallLocationKind.UserWritable, result.Kind);
        Assert.Equal(InstallLocationKind.UserWritable, InstallLocationAssessor.Assess(@"C:\x", null).Kind);
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
