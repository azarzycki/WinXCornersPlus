using System.Drawing;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;

namespace WinXCorners.Core.HotCorners;

/// <summary>One sample of the facts the engine needs. Gathered by the app on every evaluation.</summary>
/// <param name="Cursor">Pointer position in virtual-screen coordinates.</param>
/// <param name="ScreenBounds">Bounds of the monitor that contains <paramref name="Cursor"/>.</param>
/// <param name="TimestampMs">Monotonic millisecond counter (the app passes <c>Environment.TickCount64</c>).</param>
/// <param name="AnyMouseButtonDown">A mouse button is held, i.e. the user is dragging.</param>
/// <param name="ModifierSatisfied">No modifier is required, or the required one is down.</param>
/// <param name="Suppressed">The flyout or its corner picker menu is open.</param>
public readonly record struct HotCornerInput(
    Point Cursor,
    Rectangle ScreenBounds,
    long TimestampMs,
    bool AnyMouseButtonDown,
    bool ModifierSatisfied,
    bool Suppressed);

public enum HotCornerOutcome
{
    /// <summary>Nothing to do (and no countdown to show).</summary>
    Idle,

    /// <summary>The pointer is dwelling in a corner; the action fires when <see cref="HotCornerDecision.Remaining"/> reaches zero.</summary>
    Counting,

    /// <summary>Run the action now. Reported exactly once per corner visit.</summary>
    Triggered,

    /// <summary>The corner would count or fire, but a full-screen window is in the foreground.</summary>
    BlockedByFullScreen
}

/// <param name="Area">The corner, or <see cref="HotCornerArea.None"/> for <see cref="HotCornerOutcome.Idle"/>.</param>
/// <param name="ActionId">The corner's action id, or empty for <see cref="HotCornerOutcome.Idle"/>.</param>
/// <param name="Remaining">Time left before the action fires (only meaningful while counting).</param>
/// <param name="ShowCountdown">The countdown overlay should be visible.</param>
public readonly record struct HotCornerDecision(
    HotCornerOutcome Outcome,
    HotCornerArea Area,
    string ActionId,
    TimeSpan Remaining,
    bool ShowCountdown)
{
    public static HotCornerDecision Idle { get; } = new(HotCornerOutcome.Idle, HotCornerArea.None, string.Empty, TimeSpan.Zero, false);
}

/// <summary>
/// The hot-corner state machine. Pure and clock-free: time only enters through
/// <see cref="HotCornerInput.TimestampMs"/>, so every behaviour can be replayed in a test.
/// Not thread-safe; call it from one thread (the UI thread).
/// </summary>
/// <remarks>
/// Differences from the original Form1.EvaluateHotCorner, all deliberate:
/// a zero delay fires on the first qualifying sample (the original needed two); the dwell starts only
/// once the modifier is held; a disabled, empty or invalid custom command never counts down; the
/// full-screen check runs only when the dwell starts and when it is about to fire, instead of on every
/// timer tick; and arming is edge-triggered (see <see cref="Reset"/>), so a pointer that is already resting
/// in a corner when the app starts, or when settings change, does not fire that corner.
/// </remarks>
public sealed class HotCornerEngine
{
    private readonly Func<string, bool>? _isBlockedByFullScreen;

    // False until the pointer has been seen outside every corner since construction or Reset(). Corners only
    // fire on entry: a pointer that was already parked in one (at start-up, or when Reset() runs because the
    // settings changed or the session resumed) must leave first.
    private bool _armed;

    private HotCornerArea _latchedArea = HotCornerArea.None;
    private Rectangle _latchedBounds = Rectangle.Empty;

    private HotCornerArea _dwellArea = HotCornerArea.None;
    private long _dwellStartMs;

    // Set once the predicate has blocked the current corner visit, so a parked pointer does not
    // re-run the (expensive) foreground-window inspection on every tick. Cleared with the dwell.
    private bool _blockedThisVisit;

    /// <param name="isBlockedByFullScreen">
    /// Given an action id, returns true when a full-screen foreground window should block it. Only
    /// consulted when <see cref="ApplicationSettings.IgnoreFullScreen"/> is set and the corner would
    /// otherwise count or fire. Null means "never blocked".
    /// </param>
    public HotCornerEngine(Func<string, bool>? isBlockedByFullScreen = null)
    {
        _isBlockedByFullScreen = isBlockedByFullScreen;
    }

    /// <summary>Feeds one sample and returns what the app should do.</summary>
    public HotCornerDecision Evaluate(ApplicationSettings settings, in HotCornerInput input)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var area = HotCornerGeometry.GetArea(input.Cursor, input.ScreenBounds);
        if (area == HotCornerArea.None)
        {
            // Any sample outside the corners arms the engine, whatever else is going on (button held, modifier
            // up, flyout open): entering a corner from here is a real entry.
            _armed = true;
        }

        if (_latchedArea != HotCornerArea.None)
        {
            // Measured on the bounds captured when the corner fired, so a resolution or monitor change
            // while the pointer sits in the corner cannot re-arm it.
            if (HotCornerGeometry.IsWithinReleaseZone(input.Cursor, _latchedArea, _latchedBounds))
            {
                ClearDwell();
                return HotCornerDecision.Idle;
            }

            _latchedArea = HotCornerArea.None;
            _latchedBounds = Rectangle.Empty;
        }

        if (!_armed || !settings.HotCornersEnabled || input.Suppressed || input.AnyMouseButtonDown ||
            area == HotCornerArea.None || !input.ModifierSatisfied)
        {
            ClearDwell();
            return HotCornerDecision.Idle;
        }

        var actionId = HotCornerRules.GetActionId(settings, area);
        if (ActionCatalog.IsNoOp(actionId) || IsInertCustomCommand(settings, actionId))
        {
            ClearDwell();
            return HotCornerDecision.Idle;
        }

        var startingDwell = area != _dwellArea || input.TimestampMs < _dwellStartMs;
        if (startingDwell)
        {
            // A timestamp before the recorded start means the counter misbehaved; restarting is the
            // safe choice (never fire early).
            _dwellArea = area;
            _dwellStartMs = input.TimestampMs;
            _blockedThisVisit = false;
        }

        if (_blockedThisVisit)
        {
            return Decision(HotCornerOutcome.BlockedByFullScreen, area, actionId, TimeSpan.Zero, false);
        }

        var delay = HotCornerRules.GetDelay(settings, area);
        var elapsed = TimeSpan.FromMilliseconds(input.TimestampMs - _dwellStartMs);
        var due = elapsed >= delay;

        // Check when the dwell starts (so no countdown is shown over a game) and again just before
        // firing (the foreground may have changed during the delay). Never on intermediate samples.
        if ((startingDwell || due) && IsBlocked(settings, actionId))
        {
            _blockedThisVisit = true;
            return Decision(HotCornerOutcome.BlockedByFullScreen, area, actionId, TimeSpan.Zero, false);
        }

        if (!due)
        {
            var showCountdown = settings.ShowCountdown && delay > TimeSpan.Zero;
            return Decision(HotCornerOutcome.Counting, area, actionId, delay - elapsed, showCountdown);
        }

        ClearDwell();
        _latchedArea = area;
        _latchedBounds = input.ScreenBounds;
        return Decision(HotCornerOutcome.Triggered, area, actionId, TimeSpan.Zero, false);
    }

    /// <summary>
    /// Forgets the dwell and the latch, e.g. after settings change or the session resumes, and disarms: nothing
    /// counts down or fires until the pointer has been seen outside every corner, so a pointer still parked in a
    /// corner does not fire it again. (The same holds for a new engine.) Entering a corner while the modifier is
    /// not yet held does count as an entry: pressing the modifier then starts the dwell.
    /// </summary>
    public void Reset()
    {
        _armed = false;
        ClearDwell();
        _latchedArea = HotCornerArea.None;
        _latchedBounds = Rectangle.Empty;
    }

    /// <summary>
    /// A custom slot that would do nothing: custom commands are off, or the slot's command is empty or invalid
    /// (the same analysis <see cref="ActionPlanner"/> uses). Counting down for it would show a countdown that
    /// ends in nothing.
    /// </summary>
    private static bool IsInertCustomCommand(ApplicationSettings settings, string actionId)
    {
        if (!ActionCatalog.TryGetCustomIndex(actionId, out var index))
        {
            return false;
        }

        if (!settings.EnableCustomCommands)
        {
            return true;
        }

        var commands = settings.CustomCommands;
        var command = commands is not null && index < commands.Length ? commands[index]?.Command : null;
        var analysis = CustomCommandAnalyzer.Analyze(command);
        return !analysis.IsValid || analysis.Kind == CustomCommandKind.Empty;
    }

    private bool IsBlocked(ApplicationSettings settings, string actionId)
    {
        return settings.IgnoreFullScreen && _isBlockedByFullScreen is not null && _isBlockedByFullScreen(actionId);
    }

    private void ClearDwell()
    {
        _dwellArea = HotCornerArea.None;
        _dwellStartMs = 0;
        _blockedThisVisit = false;
    }

    private static HotCornerDecision Decision(HotCornerOutcome outcome, HotCornerArea area, string actionId, TimeSpan remaining, bool showCountdown)
    {
        return new HotCornerDecision(outcome, area, actionId, remaining, showCountdown);
    }
}
