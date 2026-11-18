using System.Drawing;
using WinXCorners.Core.Layout;

namespace WinXCorners.Core.Tests.Layout;

public class DpiScaleTests
{
    [Theory]
    [InlineData(96, 1.0)]
    [InlineData(120, 1.25)]
    [InlineData(144, 1.5)]
    [InlineData(192, 2.0)]
    [InlineData(0, 1.0)]
    [InlineData(-96, 1.0)]
    public void FactorFromDpi(int dpi, double expected)
    {
        Assert.Equal(expected, DpiScale.FactorFromDpi(dpi));
    }

    [Theory]
    [InlineData(10, 1.25, 13)]   // 12.5 rounds away from zero
    [InlineData(-10, 1.25, -13)]
    [InlineData(2, 1.25, 3)]     // 2.5 -> 3 (banker's rounding would give 2)
    [InlineData(17, 1.5, 26)]
    [InlineData(258, 1.75, 452)]  // 451.5
    [InlineData(16, 1.0, 16)]
    [InlineData(0, 3.0, 0)]
    public void Scale_Int(int value, double factor, int expected)
    {
        Assert.Equal(expected, DpiScale.Scale(value, factor));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Scale_InvalidFactorIsTreatedAsOne(double factor)
    {
        Assert.Equal(37, DpiScale.Scale(37, factor));
        Assert.Equal(new Size(10, 20), DpiScale.Scale(new Size(10, 20), factor));
        Assert.Equal(new Rectangle(1, 2, 3, 4), DpiScale.Scale(new Rectangle(1, 2, 3, 4), factor));
    }

    [Fact]
    public void Scale_SaturatesInsteadOfOverflowing()
    {
        Assert.Equal(int.MaxValue, DpiScale.Scale(int.MaxValue, 2.0));
        Assert.Equal(int.MinValue, DpiScale.Scale(int.MinValue, 2.0));
    }

    [Fact]
    public void Scale_SizeAndRectangleScaleEachComponent()
    {
        Assert.Equal(new Size(375, 250), DpiScale.Scale(new Size(300, 200), 1.25));
        Assert.Equal(new Rectangle(20, 21, 323, 30), DpiScale.Scale(new Rectangle(16, 17, 258, 24), 1.25));
        Assert.Equal(new Rectangle(-24, -6, 0, 3), DpiScale.Scale(new Rectangle(-16, -4, 0, 2), 1.5));
    }
}
