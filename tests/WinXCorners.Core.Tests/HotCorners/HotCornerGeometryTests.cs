using System.Drawing;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.HotCorners;

public class HotCornerGeometryTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1080);
    private static readonly Rectangle Secondary = new(-2560, -200, 2560, 1440);

    public static TheoryData<Rectangle, int> Screens()
    {
        return new TheoryData<Rectangle, int>
        {
            { Primary, 1 }, { Primary, 5 }, { Secondary, 1 }, { Secondary, 5 }
        };
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void GetArea_ExactCornerPixelsMatchAndOnePixelInwardDoesNot(Rectangle s, int size)
    {
        var l = s.Left;
        var t = s.Top;
        var r = s.Right - 1;
        var b = s.Bottom - 1;

        // The outermost pixel and the innermost pixel of each activation square.
        Assert.Equal(HotCornerArea.TopLeft, HotCornerGeometry.GetArea(new Point(l, t), s, size));
        Assert.Equal(HotCornerArea.TopLeft, HotCornerGeometry.GetArea(new Point(l + size - 1, t + size - 1), s, size));
        Assert.Equal(HotCornerArea.TopRight, HotCornerGeometry.GetArea(new Point(r, t), s, size));
        Assert.Equal(HotCornerArea.TopRight, HotCornerGeometry.GetArea(new Point(r - size + 1, t + size - 1), s, size));
        Assert.Equal(HotCornerArea.BottomLeft, HotCornerGeometry.GetArea(new Point(l, b), s, size));
        Assert.Equal(HotCornerArea.BottomLeft, HotCornerGeometry.GetArea(new Point(l + size - 1, b - size + 1), s, size));
        Assert.Equal(HotCornerArea.BottomRight, HotCornerGeometry.GetArea(new Point(r, b), s, size));
        Assert.Equal(HotCornerArea.BottomRight, HotCornerGeometry.GetArea(new Point(r - size + 1, b - size + 1), s, size));

        // One pixel inward along each axis.
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(l + size, t), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(l, t + size), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(r - size, t), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(r, t + size), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(l + size, b), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(l, b - size), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(r - size, b), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(r, b - size), s, size));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void GetArea_OnePixelOutsideTheScreenIsNotACorner(Rectangle s, int size)
    {
        // Right/Bottom are exclusive: (Right, Bottom) belongs to the next monitor.
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Left - 1, s.Top), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Left, s.Top - 1), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Right, s.Top), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Right - 1, s.Bottom), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Left, s.Bottom), s, size));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(s.Right, s.Bottom), s, size));
    }

    [Fact]
    public void GetArea_DefaultSizeIsOnePixel()
    {
        Assert.Equal(HotCornerArea.BottomRight, HotCornerGeometry.GetArea(new Point(1919, 1079), Primary));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(1918, 1079), Primary));
    }

    [Fact]
    public void GetArea_EmptyOrDegenerateBoundsOrSizeNeverMatch()
    {
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(Point.Empty, Rectangle.Empty));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(5, 5), new Rectangle(5, 5, 0, 10)));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(5, 5), new Rectangle(5, 5, 10, -1)));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(Point.Empty, Primary, 0));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(Point.Empty, Primary, -3));
    }

    [Fact]
    public void GetArea_ExtremeValuesDoNotOverflow()
    {
        var huge = new Rectangle(int.MaxValue - 10, int.MaxValue - 10, 10, 10);
        Assert.Equal(HotCornerArea.TopLeft, HotCornerGeometry.GetArea(new Point(int.MaxValue - 10, int.MaxValue - 10), huge, int.MaxValue));
        Assert.Equal(HotCornerArea.None, HotCornerGeometry.GetArea(new Point(int.MinValue, int.MinValue), Primary, int.MaxValue));
    }

    [Fact]
    public void GetArea_SinglePixelScreenPrefersTopLeft()
    {
        Assert.Equal(HotCornerArea.TopLeft, HotCornerGeometry.GetArea(new Point(3, 4), new Rectangle(3, 4, 1, 1)));
    }

    [Theory]
    [InlineData(HotCornerArea.TopLeft, 31, 31, true)]
    [InlineData(HotCornerArea.TopLeft, 32, 0, false)]
    [InlineData(HotCornerArea.TopLeft, 0, 32, false)]
    [InlineData(HotCornerArea.TopLeft, -50, -50, false)]
    [InlineData(HotCornerArea.TopLeft, -1, 0, false)]
    [InlineData(HotCornerArea.TopLeft, 0, -1, false)]
    [InlineData(HotCornerArea.TopRight, 1888, 31, true)]
    [InlineData(HotCornerArea.TopRight, 1887, 0, false)]
    [InlineData(HotCornerArea.TopRight, 1919, 32, false)]
    [InlineData(HotCornerArea.BottomLeft, 31, 1048, true)]
    [InlineData(HotCornerArea.BottomLeft, 32, 1079, false)]
    [InlineData(HotCornerArea.BottomLeft, 0, 1047, false)]
    [InlineData(HotCornerArea.BottomRight, 1888, 1048, true)]
    [InlineData(HotCornerArea.BottomRight, 1887, 1079, false)]
    [InlineData(HotCornerArea.BottomRight, 1919, 1047, false)]
    [InlineData(HotCornerArea.BottomRight, 2500, 1500, false)]
    [InlineData(HotCornerArea.BottomRight, 1920, 1079, false)]
    [InlineData(HotCornerArea.BottomRight, 1919, 1080, false)]
    [InlineData(HotCornerArea.None, 0, 0, false)]
    public void IsWithinReleaseZone_PrimaryBoundaries(HotCornerArea area, int x, int y, bool expected)
    {
        Assert.Equal(expected, HotCornerGeometry.IsWithinReleaseZone(new Point(x, y), area, Primary));
    }

    [Theory]
    [InlineData(HotCornerArea.TopLeft, -2529, -169, true)]
    [InlineData(HotCornerArea.TopLeft, -2528, -200, false)]
    [InlineData(HotCornerArea.TopRight, -32, -169, true)]
    [InlineData(HotCornerArea.TopRight, -33, -200, false)]
    [InlineData(HotCornerArea.BottomLeft, -2560, 1208, true)]
    [InlineData(HotCornerArea.BottomLeft, -2560, 1207, false)]
    [InlineData(HotCornerArea.BottomRight, -1, 1239, true)]
    [InlineData(HotCornerArea.BottomRight, -33, 1239, false)]
    public void IsWithinReleaseZone_NegativeOriginSecondary(HotCornerArea area, int x, int y, bool expected)
    {
        Assert.Equal(expected, HotCornerGeometry.IsWithinReleaseZone(new Point(x, y), area, Secondary));
    }

    /// <summary>
    /// Two monitors side by side: once the pointer has crossed onto the right-hand monitor, the left monitor's
    /// top-right release zone no longer holds it, so the right monitor's top-left corner is not blocked.
    /// </summary>
    [Fact]
    public void IsWithinReleaseZone_EndsAtTheScreenEdgeSoTheNeighbouringMonitorIsOutside()
    {
        var left = new Rectangle(-1920, 0, 1920, 1080);
        Assert.True(HotCornerGeometry.IsWithinReleaseZone(new Point(-1, 0), HotCornerArea.TopRight, left));
        Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(0, 0), HotCornerArea.TopRight, left));
        Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(5, 5), HotCornerArea.TopRight, left));
        Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(int.MaxValue, int.MinValue), HotCornerArea.TopRight, left));
    }

    [Fact]
    public void IsWithinReleaseZone_EmptyBoundsIsFalse()
    {
        Assert.False(HotCornerGeometry.IsWithinReleaseZone(Point.Empty, HotCornerArea.TopLeft, Rectangle.Empty));
    }

    [Fact]
    public void IsWithinReleaseZone_CustomZoneSize()
    {
        Assert.True(HotCornerGeometry.IsWithinReleaseZone(new Point(4, 4), HotCornerArea.TopLeft, Primary, 5));
        Assert.False(HotCornerGeometry.IsWithinReleaseZone(new Point(5, 4), HotCornerArea.TopLeft, Primary, 5));
    }
}
