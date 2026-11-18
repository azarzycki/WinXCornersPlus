using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Tests.Platform;

public class WindowsPathTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App", @"C:\Program Files\App")]
    [InlineData(@"c:/program files//app/", @"C:\program files\app")]
    [InlineData(@"  ""C:\Program Files\App\""  ", @"C:\Program Files\App")]
    [InlineData(@"C:\a\.\b\..\c", @"C:\a\c")]
    [InlineData(@"C:\..\..\Windows", @"C:\Windows")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:", @"C:")]
    [InlineData(@"C:/", @"C:\")]
    [InlineData(@"C:\\\", @"C:\")]
    [InlineData(@"C:foo\..\bar", @"C:bar")]
    [InlineData(@"\\server\share\dir\", @"\\server\share\dir")]
    [InlineData(@"//server/share/dir/../x", @"\\server\share\x")]
    [InlineData(@"\\server\share\..\..\other", @"\\server\share\other")]
    [InlineData(@"\\server\\share", @"\\server\share")]
    [InlineData(@"\\?\C:\Program Files\App", @"C:\Program Files\App")]
    [InlineData(@"\\.\C:\x", @"C:\x")]
    [InlineData(@"\\?\UNC\server\share\x", @"\\server\share\x")]
    [InlineData(@"\foo\..\bar\", @"\bar")]
    [InlineData(@"relative\path\", @"relative\path")]
    [InlineData(@"a\..\..\b", @"..\b")]
    [InlineData(@"a\..", ".")]
    [InlineData(@"\\server", @"\server")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("\"\"", "")]
    [InlineData(null, "")]
    public void Normalize_ProducesCanonicalForm(string? input, string expected)
    {
        Assert.Equal(expected, WindowsPath.Normalize(input));
    }

    [Theory]
    [InlineData(@"C:\x")]
    [InlineData(@"c:/x/../y")]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"relative")]
    public void Normalize_IsIdempotent(string input)
    {
        var once = WindowsPath.Normalize(input);
        Assert.Equal(once, WindowsPath.Normalize(once));
    }

    [Theory]
    [InlineData(@"C:\", true)]
    [InlineData(@"d:/x", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"\\?\C:\x", true)]
    [InlineData(@"C:", false)]
    [InlineData(@"C:x", false)]
    [InlineData(@"\x", false)]
    [InlineData(@"\\server", false)]
    [InlineData(@"x\y", false)]
    [InlineData(@"1:\x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsFullyQualified(string? path, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsFullyQualified(path));
    }

    [Theory]
    [InlineData(@"\\server\share", true)]
    [InlineData(@"//server/share/x", true)]
    [InlineData(@"\\?\UNC\server\share", true)]
    [InlineData(@"\\?\C:\x", false)]
    [InlineData(@"C:\x", false)]
    [InlineData(@"\x", false)]
    [InlineData(null, false)]
    public void IsUnc(string? path, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsUnc(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\App", @"C:\Program Files", true)]
    [InlineData(@"C:\Program Files", @"C:\Program Files", true)]
    [InlineData(@"c:/PROGRAM FILES/app/", @"C:\Program Files\", true)]
    [InlineData(@"C:\Program Files (x86)\App", @"C:\Program Files", false)]
    [InlineData(@"C:\Program FilesX", @"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\..\Users\x", @"C:\Program Files", false)]
    [InlineData(@"C:\Program Files\App\..\..\Users", @"C:\Program Files", false)]
    [InlineData(@"C:\Program", @"C:\Program Files", false)]
    [InlineData(@"C:\anything", @"C:\", true)]
    [InlineData(@"D:\Program Files\App", @"C:\Program Files", false)]
    [InlineData(@"\\server\share\x", @"\\SERVER\Share", true)]
    [InlineData(@"\\server\share2\x", @"\\server\share", false)]
    [InlineData(@"\\server\share\x", @"C:\", false)]
    [InlineData(@"Program Files\App", @"Program Files", false)]
    [InlineData(@"\Program Files\App", @"\Program Files", false)]
    [InlineData(@"C:\Program Files\App", "", false)]
    [InlineData(@"C:\Program Files\App", null, false)]
    [InlineData(null, @"C:\", false)]
    public void IsUnder_ComparesWholeSegmentsCaseInsensitively(string? path, string? root, bool expected)
    {
        Assert.Equal(expected, WindowsPath.IsUnder(path, root));
    }

    [Theory]
    [InlineData(@"C:\Windows", "explorer.exe", @"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Windows\", @"System32\taskmgr.exe", @"C:\Windows\System32\taskmgr.exe")]
    [InlineData(@"""C:\Program Files""", @"""App\x.exe""", @"C:\Program Files\App\x.exe")]
    [InlineData(@"C:\Windows", @"D:\other.exe", @"D:\other.exe")]
    [InlineData(@"C:\Windows", @"..\Users\x.exe", @"C:\Users\x.exe")]
    [InlineData(@"C:\Windows", "", @"C:\Windows")]
    [InlineData("", "x.exe", "x.exe")]
    [InlineData(null, null, "")]
    public void Combine_JoinsAndNormalizes(string? directory, string? file, string expected)
    {
        Assert.Equal(expected, WindowsPath.Combine(directory, file));
    }
}
