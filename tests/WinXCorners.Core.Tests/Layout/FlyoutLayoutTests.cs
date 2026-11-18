using System.Drawing;
using WinXCorners.Core.Layout;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Tests.Layout;

public class FlyoutLayoutTests
{
    private static readonly Size Flyout = new(300, 200);
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);
    private static readonly Point Anchor = new(1609, 828);

    private static readonly Rectangle BottomBar = new(0, 1040, 1920, 40);
    private static readonly Rectangle TopBar = new(0, 0, 1920, 40);
    private static readonly Rectangle LeftBar = new(0, 0, 48, 1080);
    private static readonly Rectangle RightBar = new(1872, 0, 48, 1080);

    [Theory]
    [InlineData(TaskbarEdge.Bottom, 1609, 828)]
    [InlineData(TaskbarEdge.Right, 1609, 828)]
    [InlineData(TaskbarEdge.Top, 1609, 12)]
    [InlineData(TaskbarEdge.Left, 13, 828)]
    [InlineData((TaskbarEdge)42, 1609, 828)]
    public void GetAnchor_ForEachTaskbarEdge(TaskbarEdge edge, int x, int y)
    {
        Assert.Equal(new Point(x, y), FlyoutLayout.GetAnchor(WorkArea, Flyout, edge));
    }

    [Fact]
    public void GetAnchor_RespectsWorkAreaOriginOnNegativeMonitor()
    {
        var workArea = new Rectangle(-2560, -200, 2560, 1400);
        Assert.Equal(new Point(-2560 + 13, -200 + 1400 - 212), FlyoutLayout.GetAnchor(workArea, Flyout, TaskbarEdge.Left));
        Assert.Equal(new Point(-311, -188), FlyoutLayout.GetAnchor(workArea, Flyout, TaskbarEdge.Top));
    }

    [Fact]
    public void TaskbarEdgeValuesAreTheWin32Constants()
    {
        Assert.Equal(0, (int)TaskbarEdge.Left);
        Assert.Equal(1, (int)TaskbarEdge.Top);
        Assert.Equal(2, (int)TaskbarEdge.Right);
        Assert.Equal(3, (int)TaskbarEdge.Bottom);
    }

    public static TheoryData<FlyoutAnimationDirection, TaskbarEdge, Rectangle, Point> Offscreen()
    {
        return new TheoryData<FlyoutAnimationDirection, TaskbarEdge, Rectangle, Point>
        {
            // Direction towards the taskbar: tuck behind the taskbar's outer edge.
            { FlyoutAnimationDirection.Bottom, TaskbarEdge.Bottom, BottomBar, new Point(1609, 1080) },
            { FlyoutAnimationDirection.Top, TaskbarEdge.Top, TopBar, new Point(1609, -200) },
            { FlyoutAnimationDirection.Left, TaskbarEdge.Left, LeftBar, new Point(-300, 828) },
            { FlyoutAnimationDirection.Right, TaskbarEdge.Right, RightBar, new Point(1920, 828) },

            // Any other direction: offset by the flyout's size + 1.
            { FlyoutAnimationDirection.Top, TaskbarEdge.Bottom, BottomBar, new Point(1609, 627) },
            { FlyoutAnimationDirection.Left, TaskbarEdge.Bottom, BottomBar, new Point(1308, 828) },
            { FlyoutAnimationDirection.Right, TaskbarEdge.Bottom, BottomBar, new Point(1910, 828) },
            { FlyoutAnimationDirection.Bottom, TaskbarEdge.Top, TopBar, new Point(1609, 1029) },
            { FlyoutAnimationDirection.Bottom, TaskbarEdge.Left, LeftBar, new Point(1609, 1029) },
            { FlyoutAnimationDirection.Left, TaskbarEdge.Right, RightBar, new Point(1308, 828) },
            { FlyoutAnimationDirection.Right, TaskbarEdge.Left, LeftBar, new Point(1910, 828) },
            { FlyoutAnimationDirection.Top, TaskbarEdge.Right, RightBar, new Point(1609, 627) },

            // Undefined direction (hand-edited settings): follow the taskbar edge, as the original did.
            { (FlyoutAnimationDirection)99, TaskbarEdge.Bottom, BottomBar, new Point(1609, 1080) },
            { (FlyoutAnimationDirection)99, TaskbarEdge.Top, TopBar, new Point(1609, -200) },
            { (FlyoutAnimationDirection)(-1), TaskbarEdge.Left, LeftBar, new Point(-300, 828) },
            { (FlyoutAnimationDirection)99, TaskbarEdge.Right, RightBar, new Point(1920, 828) },
            { (FlyoutAnimationDirection)99, (TaskbarEdge)7, BottomBar, new Point(1609, 1029) }
        };
    }

    [Theory]
    [MemberData(nameof(Offscreen))]
    public void GetOffscreenLocation(FlyoutAnimationDirection direction, TaskbarEdge edge, Rectangle taskbar, Point expected)
    {
        Assert.Equal(expected, FlyoutLayout.GetOffscreenLocation(Anchor, Flyout, direction, taskbar, edge));
    }
}
