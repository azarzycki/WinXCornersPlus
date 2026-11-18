using WinXCorners.Core.Platform;

namespace WinXCorners.Core.Tests.Platform;

public class ThemeCapabilitiesTests
{
    [Theory]
    [InlineData(0, false, false, null)]
    [InlineData(10240, false, false, null)]
    [InlineData(17762, false, false, null)]
    [InlineData(17763, true, false, 19)]
    [InlineData(18361, true, false, 19)]
    [InlineData(18362, true, true, 19)]
    [InlineData(18984, true, true, 19)]
    [InlineData(18985, true, true, 20)]
    [InlineData(22000, true, true, 20)]
    [InlineData(26100, true, true, 20)]
    public void ForWindowsBuild_MatchesKnownBoundaries(int build, bool darkMode, bool preferredAppMode, int? attribute)
    {
        Assert.Equal(new ThemeCapabilities(darkMode, preferredAppMode, attribute), ThemeCapabilities.ForWindowsBuild(build));
    }

    [Fact]
    public void ForWindowsBuild_NeverClaimsSetPreferredAppModeWithoutDarkModeSupport()
    {
        for (var build = 17000; build < 27000; build += 7)
        {
            var caps = ThemeCapabilities.ForWindowsBuild(build);
            Assert.True(caps.SupportsDarkModeApis || (!caps.UsesSetPreferredAppMode && caps.ImmersiveDarkModeAttribute is null));
        }
    }
}
