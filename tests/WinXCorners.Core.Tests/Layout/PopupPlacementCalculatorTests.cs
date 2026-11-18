using WinXCorners.Core.Layout;

namespace WinXCorners.Core.Tests.Layout;

public class PopupPlacementCalculatorTests
{
    [Theory]
    // anchor, height, top, bottom, preferAbove, expected
    [InlineData(500, 200, 0, 1000, true, 296)]     // prefer above, fits: 4 px gap above the anchor
    [InlineData(500, 200, 0, 1000, false, 489)]    // prefer below, fits: starts 11 px above the anchor
    [InlineData(100, 200, 0, 1000, true, 89)]      // prefer above, does not fit: falls back below
    [InlineData(900, 200, 0, 1000, false, 696)]    // prefer below, does not fit: falls back above
    [InlineData(500, 600, 0, 1000, true, 400)]     // fits nowhere, tie -> below, then pulled up into the work area
    [InlineData(700, 800, 0, 1000, false, 0)]      // fits nowhere, more room above -> above, clamped to top
    [InlineData(5, 50, 0, 1000, false, 0)]         // below position pokes above the work area
    [InlineData(100, 50, 40, 1040, true, 46)]      // offset work area (taskbar at the top)
    [InlineData(80, 50, 40, 1040, true, 69)]       // offset work area, not enough room above
    [InlineData(90, 50, 40, 1040, true, 40)]       // exactly enough room above, 4 px gap clamped to the top
    [InlineData(500, 1200, 0, 1000, true, -200)]   // taller than the work area: bottom clamp runs last
    [InlineData(-300, 100, -1080, 0, true, -404)]  // monitor above the primary (negative coordinates)
    public void CalculateTop(int anchor, int height, int top, int bottom, bool preferAbove, int expected)
    {
        Assert.Equal(expected, PopupPlacementCalculator.CalculateTop(anchor, height, top, bottom, preferAbove));
    }

    [Fact]
    public void ResultAlwaysInsideTheWorkAreaWhenTheMenuFits()
    {
        for (var anchor = -20; anchor <= 1020; anchor += 7)
        {
            foreach (var preferAbove in new[] { true, false })
            {
                var y = PopupPlacementCalculator.CalculateTop(anchor, 300, 0, 1000, preferAbove);
                Assert.InRange(y, 0, 700);
            }
        }
    }
}
