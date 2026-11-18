using System.Drawing;
using WinXCorners.Core.Layout;

namespace WinXCorners.Core.Tests.Layout;

public class FlyoutAnimationTests
{
    private const int Steps = 1000;

    [Fact]
    public void Durations()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(211), FlyoutAnimation.OpenDuration);
        Assert.Equal(TimeSpan.FromMilliseconds(229), FlyoutAnimation.CloseDuration);
        Assert.Equal(FlyoutAnimation.OpenDuration, FlyoutAnimation.Duration(closing: false));
        Assert.Equal(FlyoutAnimation.CloseDuration, FlyoutAnimation.Duration(closing: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveProgress_GoesFromZeroToOneMonotonically(bool closing)
    {
        Assert.Equal(0, FlyoutAnimation.MoveProgress(0, closing));
        Assert.Equal(1, FlyoutAnimation.MoveProgress(1, closing));
        var previous = 0.0;
        for (var i = 0; i <= Steps; i++)
        {
            var value = FlyoutAnimation.MoveProgress(i / (double)Steps, closing);
            Assert.InRange(value, previous, 1);
            previous = value;
        }
    }

    [Fact]
    public void MoveProgress_OpeningDeceleratesClosingAccelerates()
    {
        Assert.True(FlyoutAnimation.MoveProgress(0.5, closing: false) > 0.9);
        Assert.Equal(0.25, FlyoutAnimation.MoveProgress(0.5, closing: true), 10);
    }

    [Fact]
    public void Opacity_OpeningRisesToOneAtSeventyPercent()
    {
        Assert.Equal(0, FlyoutAnimation.Opacity(0, closing: false));
        Assert.Equal(1, FlyoutAnimation.Opacity(0.7, closing: false), 10);
        Assert.Equal(1, FlyoutAnimation.Opacity(1, closing: false));
        var previous = 0.0;
        for (var i = 0; i <= Steps; i++)
        {
            var value = FlyoutAnimation.Opacity(i / (double)Steps, closing: false);
            Assert.InRange(value, previous, 1);
            previous = value;
        }
    }

    [Fact]
    public void Opacity_ClosingHoldsThenFallsToZero()
    {
        Assert.Equal(1, FlyoutAnimation.Opacity(0, closing: true));
        Assert.Equal(1, FlyoutAnimation.Opacity(0.45, closing: true));
        Assert.Equal(0, FlyoutAnimation.Opacity(1, closing: true));
        var previous = 1.0;
        for (var i = 0; i <= Steps; i++)
        {
            var value = FlyoutAnimation.Opacity(i / (double)Steps, closing: true);
            Assert.InRange(value, 0, previous);
            previous = value;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutOfRangeAndNaNInputAreClamped(bool closing)
    {
        Assert.Equal(FlyoutAnimation.MoveProgress(0, closing), FlyoutAnimation.MoveProgress(-5, closing));
        Assert.Equal(FlyoutAnimation.MoveProgress(1, closing), FlyoutAnimation.MoveProgress(7.5, closing));
        Assert.Equal(FlyoutAnimation.MoveProgress(1, closing), FlyoutAnimation.MoveProgress(double.PositiveInfinity, closing));
        Assert.Equal(FlyoutAnimation.MoveProgress(0, closing), FlyoutAnimation.MoveProgress(double.NaN, closing));
        Assert.Equal(FlyoutAnimation.Opacity(0, closing), FlyoutAnimation.Opacity(-1, closing));
        Assert.Equal(FlyoutAnimation.Opacity(1, closing), FlyoutAnimation.Opacity(3, closing));
        Assert.Equal(FlyoutAnimation.Opacity(0, closing), FlyoutAnimation.Opacity(double.NaN, closing));
    }

    [Fact]
    public void Interpolate_EndsAndMidpoints()
    {
        var start = new Point(0, 0);
        var end = new Point(100, -50);
        Assert.Equal(start, FlyoutAnimation.Interpolate(start, end, 0));
        Assert.Equal(end, FlyoutAnimation.Interpolate(start, end, 1));
        Assert.Equal(new Point(50, -25), FlyoutAnimation.Interpolate(start, end, 0.5));
        Assert.Equal(new Point(33, -17), FlyoutAnimation.Interpolate(start, end, 0.333));
    }

    [Fact]
    public void Interpolate_ClampsProgress()
    {
        var start = new Point(1609, 1080);
        var end = new Point(1609, 828);
        Assert.Equal(start, FlyoutAnimation.Interpolate(start, end, -0.5));
        Assert.Equal(end, FlyoutAnimation.Interpolate(start, end, 1.5));
        Assert.Equal(start, FlyoutAnimation.Interpolate(start, end, double.NaN));
    }

    [Fact]
    public void Interpolate_ExtremeCoordinatesDoNotOverflow()
    {
        var start = new Point(int.MinValue, int.MaxValue);
        var end = new Point(int.MaxValue, int.MinValue);
        Assert.Equal(end, FlyoutAnimation.Interpolate(start, end, 1));
        Assert.Equal(start, FlyoutAnimation.Interpolate(start, end, 0));
    }

    [Fact]
    public void Interpolate_IsMonotonicAlongTheEasedPath()
    {
        var start = new Point(1609, 1080);
        var end = new Point(1609, 828);
        var previousY = start.Y;
        for (var i = 0; i <= Steps; i++)
        {
            var p = FlyoutAnimation.Interpolate(start, end, FlyoutAnimation.MoveProgress(i / (double)Steps, closing: false));
            Assert.InRange(p.Y, end.Y, previousY);
            previousY = p.Y;
        }
    }
}
