using System.Drawing;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.Layout;

/// <summary>Where the tray flyout sits and where it slides in from / out to.</summary>
public static class FlyoutLayout
{
    /// <summary>Gap between the flyout and the work-area edges.</summary>
    public const int Margin = 12;

    /// <summary>The original nudged the flyout 1 px right to line up with the Windows 10 flyouts.</summary>
    public const int HorizontalOffset = 1;

    /// <summary>
    /// Resting position (top-left) of the flyout: next to the taskbar, in the corner nearest the
    /// notification area. An unknown edge is treated as <see cref="TaskbarEdge.Bottom"/>.
    /// </summary>
    public static Point GetAnchor(Rectangle workArea, Size flyoutSize, TaskbarEdge edge)
    {
        var right = workArea.Right - flyoutSize.Width - Margin + HorizontalOffset;
        var bottom = workArea.Bottom - flyoutSize.Height - Margin;
        return edge switch
        {
            TaskbarEdge.Top => new Point(right, workArea.Top + Margin),
            TaskbarEdge.Left => new Point(workArea.Left + Margin + HorizontalOffset, bottom),
            _ => new Point(right, bottom)
        };
    }

    /// <summary>
    /// The point the flyout slides in from and out to. When the direction points at the taskbar the
    /// flyout tucks just behind the taskbar's outer edge; otherwise it is offset by its own size + 1
    /// in that direction. An undefined <paramref name="direction"/> follows the taskbar edge, as the
    /// original did.
    /// </summary>
    public static Point GetOffscreenLocation(
        Point anchor,
        Size flyoutSize,
        FlyoutAnimationDirection direction,
        Rectangle taskbarBounds,
        TaskbarEdge taskbarEdge)
    {
        if (!Enum.IsDefined(direction))
        {
            direction = taskbarEdge switch
            {
                TaskbarEdge.Top => FlyoutAnimationDirection.Top,
                TaskbarEdge.Left => FlyoutAnimationDirection.Left,
                TaskbarEdge.Right => FlyoutAnimationDirection.Right,
                _ => FlyoutAnimationDirection.Bottom
            };
        }

        switch (direction)
        {
            case FlyoutAnimationDirection.Bottom when taskbarEdge == TaskbarEdge.Bottom:
                return new Point(anchor.X, taskbarBounds.Bottom);
            case FlyoutAnimationDirection.Top when taskbarEdge == TaskbarEdge.Top:
                return new Point(anchor.X, taskbarBounds.Top - flyoutSize.Height);
            case FlyoutAnimationDirection.Left when taskbarEdge == TaskbarEdge.Left:
                return new Point(taskbarBounds.Left - flyoutSize.Width, anchor.Y);
            case FlyoutAnimationDirection.Right when taskbarEdge == TaskbarEdge.Right:
                return new Point(taskbarBounds.Right, anchor.Y);
        }

        return direction switch
        {
            FlyoutAnimationDirection.Top => new Point(anchor.X, anchor.Y - (flyoutSize.Height + 1)),
            FlyoutAnimationDirection.Left => new Point(anchor.X - (flyoutSize.Width + 1), anchor.Y),
            FlyoutAnimationDirection.Right => new Point(anchor.X + flyoutSize.Width + 1, anchor.Y),
            _ => new Point(anchor.X, anchor.Y + flyoutSize.Height + 1)
        };
    }
}
