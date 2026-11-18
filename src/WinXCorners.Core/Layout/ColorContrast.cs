using System.Drawing;

namespace WinXCorners.Core.Layout;

/// <summary>
/// WCAG 2 relative luminance and contrast ratio, used to choose readable text on a colour the user picked (the
/// countdown badge takes the Windows accent colour). Alpha is ignored: the colours are drawn opaque.
/// </summary>
public static class ColorContrast
{
    /// <summary>Relative luminance in [0, 1] (WCAG 2.x, sRGB).</summary>
    public static double RelativeLuminance(Color color)
    {
        static double Linear(byte channel)
        {
            var c = channel / 255d;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    /// <summary>Contrast ratio in [1, 21]; symmetric in its arguments.</summary>
    public static double Ratio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// Whichever of <paramref name="dark"/> and <paramref name="light"/> contrasts more with
    /// <paramref name="background"/>; <paramref name="light"/> on a tie. Choosing by the actual ratio instead of a fixed
    /// luminance threshold always gives the better of the two (a threshold of 0.45 gave white text below 3:1 on
    /// mid-tone accents such as orange).
    /// </summary>
    public static Color ReadableText(Color background, Color dark, Color light) =>
        Ratio(background, dark) > Ratio(background, light) ? dark : light;
}
