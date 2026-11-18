using System.Drawing;

namespace WinXCorners.Core.Layout;

/// <summary>
/// Timing curves of the flyout slide/fade (the original "legacy" style, the only one the app used).
/// All functions clamp their progress argument to 0..1, so a late timer tick cannot overshoot.
/// </summary>
public static class FlyoutAnimation
{
    public static TimeSpan OpenDuration { get; } = TimeSpan.FromMilliseconds(211);

    public static TimeSpan CloseDuration { get; } = TimeSpan.FromMilliseconds(229);

    public static TimeSpan Duration(bool closing) => closing ? CloseDuration : OpenDuration;

    /// <summary>
    /// Fraction of the way from start to end at <paramref name="progress"/> (elapsed / duration).
    /// Opening decelerates (ease-out expo), closing accelerates (ease-in quad).
    /// </summary>
    public static double MoveProgress(double progress, bool closing)
    {
        var p = Clamp01(progress);
        return closing ? EaseInQuad(p) : EaseOutExpo(p);
    }

    /// <summary>
    /// Window opacity at <paramref name="progress"/>. Opening fades in over the first 70%; closing stays
    /// opaque for the first 45% and then fades out. Opening goes 0 -> 1, closing 1 -> 0.
    /// </summary>
    public static double Opacity(double progress, bool closing)
    {
        var p = Clamp01(progress);
        return closing
            ? 1 - EaseInQuad(Math.Max(0, (p - 0.45) / 0.55))
            : EaseOutQuad(Math.Min(1, p / 0.7));
    }

    /// <summary>Point on the segment start..end; coordinates rounded as the original did.</summary>
    public static Point Interpolate(Point start, Point end, double progress)
    {
        var p = Clamp01(progress);
        return new Point(Lerp(start.X, end.X, p), Lerp(start.Y, end.Y, p));
    }

    // Done in double so the distance between far-apart coordinates cannot overflow an int.
    private static int Lerp(int start, int end, double p)
    {
        var value = start + Math.Round((end - (double)start) * p);
        return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
    }

    // NaN is treated as "not started" so a zero-length duration (0/0) cannot poison the window position.
    private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    private static double EaseOutQuad(double value) => 1 - ((1 - value) * (1 - value));

    private static double EaseInQuad(double value) => value * value;

    // 1 - 2^-10 is 0.999..., so the end point is pinned to exactly 1.
    private static double EaseOutExpo(double value) => value >= 1 ? 1 : 1 - Math.Pow(2, -10 * value);
}
