using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.HotCorners;

public class HotCornerRulesTests
{
    [Fact]
    public void GetActionId_MapsEachCorner()
    {
        var s = new ApplicationSettings
        {
            TopLeftActionId = "a", TopRightActionId = "b", BottomLeftActionId = "c", BottomRightActionId = "d"
        };

        Assert.Equal("a", HotCornerRules.GetActionId(s, HotCornerArea.TopLeft));
        Assert.Equal("b", HotCornerRules.GetActionId(s, HotCornerArea.TopRight));
        Assert.Equal("c", HotCornerRules.GetActionId(s, HotCornerArea.BottomLeft));
        Assert.Equal("d", HotCornerRules.GetActionId(s, HotCornerArea.BottomRight));
        Assert.Equal(string.Empty, HotCornerRules.GetActionId(s, HotCornerArea.None));
        Assert.Equal(string.Empty, HotCornerRules.GetActionId(s, (HotCornerArea)99));
    }

    [Fact]
    public void GetActionId_NullPropertyFromUnsanitizedJsonIsEmpty()
    {
        var s = new ApplicationSettings { TopLeftActionId = null! };
        Assert.Equal(string.Empty, HotCornerRules.GetActionId(s, HotCornerArea.TopLeft));
    }

    [Fact]
    public void NullSettingsThrow()
    {
        Assert.Throws<ArgumentNullException>(() => HotCornerRules.GetActionId(null!, HotCornerArea.TopLeft));
        Assert.Throws<ArgumentNullException>(() => HotCornerRules.GetDelay(null!, HotCornerArea.TopLeft));
    }

    [Theory]
    [InlineData(0, 250)]
    [InlineData(1, 500)]
    [InlineData(2, 750)]
    [InlineData(3, 1000)]
    [InlineData(4, 1250)]
    [InlineData(5, 1500)]
    [InlineData(6, 1000)]
    [InlineData(-1, 1000)]
    [InlineData(int.MaxValue, 1000)]
    [InlineData(int.MinValue, 1000)]
    public void DelayFromIndex(int index, int expectedMs)
    {
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), HotCornerRules.DelayFromIndex(index));
    }

    [Fact]
    public void DelayLabels_AreIndexAlignedWithDelays()
    {
        Assert.Equal(["0.25", "0.50", "0.75", "1.00", "1.25", "1.50"], HotCornerRules.DelayLabels);
        Assert.Equal(ApplicationSettings.DelayOptionCount, HotCornerRules.DelayLabels.Count);
        for (var i = 0; i < HotCornerRules.DelayLabels.Count; i++)
        {
            Assert.Equal(double.Parse(HotCornerRules.DelayLabels[i], System.Globalization.CultureInfo.InvariantCulture),
                HotCornerRules.DelayFromIndex(i).TotalSeconds);
        }
    }

    [Fact]
    public void GetDelay_NothingEnabledIsZeroEvenWithIndexesSet()
    {
        var s = new ApplicationSettings { TopLeftDelayIndex = 5 };
        foreach (var area in Enum.GetValues<HotCornerArea>())
        {
            Assert.Equal(TimeSpan.Zero, HotCornerRules.GetDelay(s, area));
        }
    }

    [Fact]
    public void GetDelay_PerCornerOnlyForEnabledCorners()
    {
        var s = new ApplicationSettings
        {
            TopLeftDelayEnabled = true, TopLeftDelayIndex = 0,
            TopRightDelayEnabled = true, TopRightDelayIndex = 1,
            BottomLeftDelayEnabled = true, BottomLeftDelayIndex = 2,
            BottomRightDelayEnabled = false, BottomRightDelayIndex = 5
        };

        Assert.Equal(TimeSpan.FromMilliseconds(250), HotCornerRules.GetDelay(s, HotCornerArea.TopLeft));
        Assert.Equal(TimeSpan.FromMilliseconds(500), HotCornerRules.GetDelay(s, HotCornerArea.TopRight));
        Assert.Equal(TimeSpan.FromMilliseconds(750), HotCornerRules.GetDelay(s, HotCornerArea.BottomLeft));
        Assert.Equal(TimeSpan.Zero, HotCornerRules.GetDelay(s, HotCornerArea.BottomRight));

        s.BottomRightDelayEnabled = true;
        Assert.Equal(TimeSpan.FromMilliseconds(1500), HotCornerRules.GetDelay(s, HotCornerArea.BottomRight));
    }

    [Fact]
    public void GetDelay_GlobalOverridesEveryCorner()
    {
        var s = new ApplicationSettings
        {
            GlobalDelayEnabled = true, GlobalDelayIndex = 4,
            TopLeftDelayEnabled = true, TopLeftDelayIndex = 0
        };

        Assert.Equal(TimeSpan.FromMilliseconds(1250), HotCornerRules.GetDelay(s, HotCornerArea.TopLeft));
        Assert.Equal(TimeSpan.FromMilliseconds(1250), HotCornerRules.GetDelay(s, HotCornerArea.BottomRight));
    }

    [Fact]
    public void GetDelay_HostileIndexFallsBackToOneSecond()
    {
        var s = new ApplicationSettings { GlobalDelayEnabled = true, GlobalDelayIndex = -7 };
        Assert.Equal(TimeSpan.FromSeconds(1), HotCornerRules.GetDelay(s, HotCornerArea.TopLeft));
    }
}
