using System.Drawing;

namespace WinXCorners.Core.Layout;

/// <summary>
/// Scaling of 96-DPI design-time layout values to the monitor's DPI. Replaces the copies that lived in
/// the flyout, Settings and About forms.
/// </summary>
public static class DpiScale
{
    /// <summary>The DPI at which a scale factor is 1.</summary>
    public const int BaseDpi = 96;

    /// <summary><paramref name="dpi"/> / 96; a non-positive DPI (a failed query) gives 1.</summary>
    public static double FactorFromDpi(int dpi) => dpi > 0 ? dpi / (double)BaseDpi : 1.0;

    /// <summary>
    /// <paramref name="value"/> x <paramref name="factor"/>, rounded half away from zero. A factor that is
    /// not a positive finite number is treated as 1. Results beyond the int range saturate.
    /// </summary>
    public static int Scale(int value, double factor)
    {
        var scaled = Math.Round(value * Normalize(factor), MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
    }

    public static Size Scale(Size baseSize, double factor)
    {
        return new Size(Scale(baseSize.Width, factor), Scale(baseSize.Height, factor));
    }

    /// <summary>Scales position and size independently, as the original form layout code did.</summary>
    public static Rectangle Scale(Rectangle baseBounds, double factor)
    {
        return new Rectangle(
            Scale(baseBounds.X, factor),
            Scale(baseBounds.Y, factor),
            Scale(baseBounds.Width, factor),
            Scale(baseBounds.Height, factor));
    }

    private static double Normalize(double factor) => double.IsFinite(factor) && factor > 0 ? factor : 1.0;
}
