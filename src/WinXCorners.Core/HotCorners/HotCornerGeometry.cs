using System.Drawing;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.HotCorners;

/// <summary>
/// Pixel geometry of the hot corners. <see cref="Rectangle.Right"/> and <see cref="Rectangle.Bottom"/>
/// are exclusive, so the bottom-right pixel of a screen is (Right - 1, Bottom - 1). Screens left of or
/// above the primary have negative origins; nothing here assumes (0, 0).
/// </summary>
public static class HotCornerGeometry
{
    /// <summary>Edge length in pixels of the square that activates a corner.</summary>
    public const int ActivationSize = 1;

    /// <summary>
    /// After a corner fires, the pointer must leave a square of this size before the corner can fire
    /// again, so jitter at the edge cannot re-trigger the action.
    /// </summary>
    public const int RearmReleaseSize = 32;

    /// <summary>
    /// The corner whose <paramref name="size"/> x <paramref name="size"/> square contains
    /// <paramref name="cursor"/>, or <see cref="HotCornerArea.None"/>. Empty bounds or a
    /// non-positive size never match.
    /// </summary>
    public static HotCornerArea GetArea(Point cursor, Rectangle screenBounds, int size = ActivationSize)
    {
        if (screenBounds.IsEmpty || screenBounds.Width <= 0 || screenBounds.Height <= 0 || size <= 0)
        {
            return HotCornerArea.None;
        }

        // Compare in long so huge sizes or extreme coordinates cannot overflow.
        long x = cursor.X;
        long y = cursor.Y;
        var inLeft = x >= screenBounds.Left && x < (long)screenBounds.Left + size;
        var inRight = x <= (long)screenBounds.Right - 1 && x > (long)screenBounds.Right - size - 1;
        var inTop = y >= screenBounds.Top && y < (long)screenBounds.Top + size;
        var inBottom = y <= (long)screenBounds.Bottom - 1 && y > (long)screenBounds.Bottom - size - 1;

        // Same precedence as the original when a tiny screen makes the squares overlap.
        if (inLeft && inTop)
        {
            return HotCornerArea.TopLeft;
        }

        if (inRight && inTop)
        {
            return HotCornerArea.TopRight;
        }

        if (inLeft && inBottom)
        {
            return HotCornerArea.BottomLeft;
        }

        return inRight && inBottom ? HotCornerArea.BottomRight : HotCornerArea.None;
    }

    /// <summary>
    /// True while <paramref name="cursor"/> is still inside the release zone of a corner that has fired:
    /// the <paramref name="zone"/> x <paramref name="zone"/> square in that corner of <paramref name="bounds"/>.
    /// The zone ends at the screen edge. A pointer that has moved past it (on a multi-monitor setup, onto the
    /// neighbouring monitor) is outside, so that monitor's own adjacent corner is not blocked by this one.
    /// Empty bounds or <see cref="HotCornerArea.None"/> -> false.
    /// </summary>
    public static bool IsWithinReleaseZone(Point cursor, HotCornerArea area, Rectangle bounds, int zone = RearmReleaseSize)
    {
        if (bounds.IsEmpty)
        {
            return false;
        }

        long x = cursor.X;
        long y = cursor.Y;
        if (x < bounds.Left || x >= (long)bounds.Right || y < bounds.Top || y >= (long)bounds.Bottom)
        {
            return false;
        }

        var nearLeft = x <= (long)bounds.Left + zone - 1;
        var nearRight = x >= (long)bounds.Right - zone;
        var nearTop = y <= (long)bounds.Top + zone - 1;
        var nearBottom = y >= (long)bounds.Bottom - zone;

        return area switch
        {
            HotCornerArea.TopLeft => nearLeft && nearTop,
            HotCornerArea.TopRight => nearRight && nearTop,
            HotCornerArea.BottomLeft => nearLeft && nearBottom,
            HotCornerArea.BottomRight => nearRight && nearBottom,
            _ => false
        };
    }
}
