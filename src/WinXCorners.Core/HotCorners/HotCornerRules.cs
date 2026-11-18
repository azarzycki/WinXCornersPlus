using System.Globalization;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.HotCorners;

/// <summary>Per-corner settings lookups: which action a corner runs and how long it must be held.</summary>
public static class HotCornerRules
{
    private static readonly double[] DelaySeconds = [0.25, 0.50, 0.75, 1.00, 1.25, 1.50];

    /// <summary>Fallback for an out-of-range index, matching the original (and the default index 3).</summary>
    private const double FallbackDelaySeconds = 1.00;

    /// <summary>Labels for the delay combo boxes, index-aligned with <see cref="DelayFromIndex"/>.</summary>
    public static IReadOnlyList<string> DelayLabels { get; } =
        Array.AsReadOnly(DelaySeconds.Select(static s => s.ToString("0.00", CultureInfo.InvariantCulture)).ToArray());

    /// <summary>The action id configured for <paramref name="area"/>; empty for None or a null property.</summary>
    public static string GetActionId(ApplicationSettings settings, HotCornerArea area)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var id = area switch
        {
            HotCornerArea.TopLeft => settings.TopLeftActionId,
            HotCornerArea.TopRight => settings.TopRightActionId,
            HotCornerArea.BottomLeft => settings.BottomLeftActionId,
            HotCornerArea.BottomRight => settings.BottomRightActionId,
            _ => string.Empty
        };

        return id ?? string.Empty;
    }

    /// <summary>
    /// The dwell time before <paramref name="area"/> fires. An enabled global delay overrides every
    /// corner; otherwise a corner's own delay applies only when that corner's delay is enabled.
    /// </summary>
    public static TimeSpan GetDelay(ApplicationSettings settings, HotCornerArea area)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.GlobalDelayEnabled)
        {
            return DelayFromIndex(settings.GlobalDelayIndex);
        }

        return area switch
        {
            HotCornerArea.TopLeft when settings.TopLeftDelayEnabled => DelayFromIndex(settings.TopLeftDelayIndex),
            HotCornerArea.TopRight when settings.TopRightDelayEnabled => DelayFromIndex(settings.TopRightDelayIndex),
            HotCornerArea.BottomLeft when settings.BottomLeftDelayEnabled => DelayFromIndex(settings.BottomLeftDelayIndex),
            HotCornerArea.BottomRight when settings.BottomRightDelayEnabled => DelayFromIndex(settings.BottomRightDelayIndex),
            _ => TimeSpan.Zero
        };
    }

    /// <summary>0 -> 0.25 s ... 5 -> 1.50 s in 0.25 s steps; any other index -> 1.00 s.</summary>
    public static TimeSpan DelayFromIndex(int index)
    {
        var seconds = index >= 0 && index < DelaySeconds.Length ? DelaySeconds[index] : FallbackDelaySeconds;
        return TimeSpan.FromSeconds(seconds);
    }
}
