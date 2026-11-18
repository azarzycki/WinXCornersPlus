namespace WinXCorners.Core.Layout;

/// <summary>Vertical placement of the corner picker menu next to the button that opened it.</summary>
public static class PopupPlacementCalculator
{
    /// <summary>
    /// The menu's top edge: above the anchor (4 px gap) or overlapping it from 11 px above (below),
    /// honouring <paramref name="preferAbove"/> when the menu fits, otherwise whichever side fits, otherwise
    /// the roomier side; finally clamped into the work area. If the menu is taller than the work area its bottom is aligned
    /// with the work-area bottom (the bottom clamp runs last), as in the original.
    /// </summary>
    public static int CalculateTop(
        int anchorTop,
        int menuHeight,
        int workAreaTop,
        int workAreaBottom,
        bool preferAbove)
    {
        var yAbove = anchorTop - menuHeight - 4;
        var yBelow = anchorTop - 11;

        var availableAbove = anchorTop - workAreaTop;
        var availableBelow = workAreaBottom - anchorTop;

        int y;
        if (preferAbove && availableAbove >= menuHeight)
        {
            y = yAbove;
        }
        else if (!preferAbove && availableBelow >= menuHeight)
        {
            y = yBelow;
        }
        else if (availableBelow >= menuHeight)
        {
            y = yBelow;
        }
        else if (availableAbove >= menuHeight)
        {
            y = yAbove;
        }
        else
        {
            y = availableBelow >= availableAbove ? yBelow : yAbove;
        }

        if (y < workAreaTop)
        {
            y = workAreaTop;
        }

        if (y + menuHeight > workAreaBottom)
        {
            y = workAreaBottom - menuHeight;
        }

        return y;
    }
}
