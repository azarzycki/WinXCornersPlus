using WinXCorners.Core.Settings;

namespace WinXCorners.Core.Tests.Settings;

public class SettingsPathsTests
{
    [Fact]
    public void ForApp_PutsPrimaryInAnAppFolderUnderRoamingAndLegacyBesideTheExe()
    {
        var roaming = Path.Combine("home", "user", "Roaming");
        var appBase = Path.Combine("opt", "wxc");

        var paths = SettingsPaths.ForApp(roaming, appBase);

        Assert.Equal(Path.Combine(roaming, "WinXCornersPlus", "settings.json"), paths.PrimaryPath);
        Assert.Equal(Path.Combine(appBase, "settings.json"), paths.LegacyPath);
    }

    [Fact]
    public void Constants_KeepTheirPublishedValues()
    {
        Assert.Equal("WinXCornersPlus", SettingsPaths.FolderName);
        Assert.Equal("settings.json", SettingsPaths.FileName);
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("x", " ")]
    public void ForApp_RejectsMissingDirectories(string? roaming, string? appBase)
    {
        Assert.ThrowsAny<ArgumentException>(() => SettingsPaths.ForApp(roaming!, appBase!));
    }
}
