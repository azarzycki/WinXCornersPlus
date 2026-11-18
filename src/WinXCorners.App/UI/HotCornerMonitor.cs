using WinXCorners.Core.Actions;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Glue between the input facts and Core's <see cref="HotCornerEngine"/>: samples the facts, asks the engine,
/// and turns its decision into countdown and trigger callbacks (review B4: no low-level hook; this runs on the
/// UI thread from WM_INPUT and a 50 ms timer, and only queues actions, never runs them).
/// </summary>
/// <remarks>
/// Every fact comes from a delegate, so Windows CI can drive it with synthetic cursor positions and a fake clock.
/// Not thread-safe: call <see cref="Evaluate"/> and <see cref="Reset"/> on one thread.
/// </remarks>
internal sealed class HotCornerMonitor
{
    // Win32 VK_SHIFT / VK_CONTROL / VK_MENU: either side of the key counts, as in the original.
    private const ushort VkShift = 0x10;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;

    private readonly Func<ApplicationSettings> _settings;
    private readonly Func<Point?> _cursor;
    private readonly Func<bool> _inputDesktopIsOurs;
    private readonly Func<Point, Rectangle> _screenBoundsAt;
    private readonly Func<bool> _anyMouseButtonDown;
    private readonly Func<ushort, bool> _isKeyDown;
    private readonly Func<long> _clock;
    private readonly Func<bool> _suppressed;
    private readonly BoundedLog _log;
    private readonly HotCornerEngine _engine;
    private bool _countdownVisible;
    private HotCornerOutcome _lastOutcome = HotCornerOutcome.Idle;
    private HotCornerArea _lastBlockedArea = HotCornerArea.None;
    private bool _pointerWasAvailable = true;

    /// <param name="settings">The live settings (read on every sample).</param>
    /// <param name="cursor">
    /// Pointer position in virtual-screen coordinates, or null when it cannot be read (GetCursorPos fails while another
    /// desktop has the input; review V2 D2).
    /// </param>
    /// <param name="screenBoundsAt">Bounds of the monitor containing a point.</param>
    /// <param name="anyMouseButtonDown">Whether a mouse button is held.</param>
    /// <param name="isKeyDown">Whether a virtual key is down (for the modifier key).</param>
    /// <param name="clock">Monotonic milliseconds (<c>Environment.TickCount64</c>).</param>
    /// <param name="suppressed">True while the flyout or its corner menu is open.</param>
    /// <param name="isFullScreenForeground">Whether a full-screen window is in front (only asked when it matters).</param>
    /// <param name="log">Where triggers and full-screen blocks are logged.</param>
    /// <param name="inputDesktopIsOurs">Whether the input desktop is the one this app runs on; null means always.</param>
    internal HotCornerMonitor(
        Func<ApplicationSettings> settings,
        Func<Point?> cursor,
        Func<Point, Rectangle> screenBoundsAt,
        Func<bool> anyMouseButtonDown,
        Func<ushort, bool> isKeyDown,
        Func<long> clock,
        Func<bool> suppressed,
        Func<bool> isFullScreenForeground,
        BoundedLog log,
        Func<bool>? inputDesktopIsOurs = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _screenBoundsAt = screenBoundsAt ?? throw new ArgumentNullException(nameof(screenBoundsAt));
        _anyMouseButtonDown = anyMouseButtonDown ?? throw new ArgumentNullException(nameof(anyMouseButtonDown));
        _isKeyDown = isKeyDown ?? throw new ArgumentNullException(nameof(isKeyDown));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _suppressed = suppressed ?? throw new ArgumentNullException(nameof(suppressed));
        ArgumentNullException.ThrowIfNull(isFullScreenForeground);
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _inputDesktopIsOurs = inputDesktopIsOurs ?? (static () => true);
        _engine = new HotCornerEngine(_ => isFullScreenForeground());
    }

    /// <summary>Lock and connection state of this session, fed by <see cref="OnSessionChange"/>.</summary>
    internal SessionActivity Session { get; } = new();

    /// <summary>Show (or update) the countdown badge: screen bounds, corner, time left.</summary>
    internal event Action<Rectangle, HotCornerArea, TimeSpan>? CountdownRequested;

    /// <summary>Hide the countdown badge. Raised only when it was requested before.</summary>
    internal event Action? CountdownHidden;

    /// <summary>Run this action id now. Raised once per corner visit.</summary>
    internal event Action<string>? ActionTriggered;

    /// <summary>Samples every fact once, asks the engine and raises the matching callbacks.</summary>
    internal HotCornerDecision Evaluate()
    {
        var settings = _settings();
        var position = _cursor();
        // Only ask about the desktop when the rest says the pointer is there: it costs a few system calls.
        var available = Session.IsPointerAvailable(position.HasValue, position.HasValue && Session.IsActive && _inputDesktopIsOurs());
        if (available != _pointerWasAvailable)
        {
            _pointerWasAvailable = available;
            _log.Add(available
                ? "Hot corners resumed: the pointer is back on this desktop. A corner fires only after the pointer has left it."
                : "Hot corners paused: the session is locked or disconnected, or another desktop has the pointer.");
        }

        var cursor = position ?? Point.Empty;
        var bounds = available ? _screenBoundsAt(cursor) : Rectangle.Empty;
        var input = new HotCornerInput(
            cursor,
            bounds,
            _clock(),
            available && _anyMouseButtonDown(),
            available && IsModifierSatisfied(settings.HotCornerModifierKey, _isKeyDown),
            _suppressed(),
            available);

        var decision = _engine.Evaluate(settings, in input);
        switch (decision.Outcome)
        {
            case HotCornerOutcome.Counting when decision.ShowCountdown:
                _countdownVisible = true;
                CountdownRequested?.Invoke(bounds, decision.Area, decision.Remaining);
                break;
            case HotCornerOutcome.Triggered:
                HideCountdown();
                _log.Add($"Running '{Describe(settings, decision.ActionId)}' from the {decision.Area} corner.");
                ActionTriggered?.Invoke(decision.ActionId);
                break;
            case HotCornerOutcome.BlockedByFullScreen:
                HideCountdown();
                if (_lastOutcome != HotCornerOutcome.BlockedByFullScreen || _lastBlockedArea != decision.Area)
                {
                    _log.Add($"'{Describe(settings, decision.ActionId)}' not run: a full-screen window is in front.");
                }

                _lastBlockedArea = decision.Area;
                break;
            default:
                HideCountdown();
                break;
        }

        _lastOutcome = decision.Outcome;
        return decision;
    }

    /// <summary>
    /// A WM_WTSSESSION_CHANGE notification. Locking or disconnecting pauses hot corners at once (the next evaluation
    /// also sees it); after unlocking, a corner fires only once the pointer has been seen outside every corner.
    /// </summary>
    internal void OnSessionChange(SessionChange change)
    {
        if (Session.Apply(change))
        {
            Reset();
        }
    }

    /// <summary>Forgets any dwell and latch (settings changed, display changed) and hides the badge.</summary>
    internal void Reset()
    {
        _engine.Reset();
        _lastOutcome = HotCornerOutcome.Idle;
        _lastBlockedArea = HotCornerArea.None;
        HideCountdown();
    }

    /// <summary>
    /// True when no modifier is configured or the configured one is down. An out-of-range value (the sanitizer
    /// should have fixed it) behaves like None, as the original did.
    /// </summary>
    internal static bool IsModifierSatisfied(HotCornerModifierKey modifier, Func<ushort, bool> isKeyDown)
    {
        ArgumentNullException.ThrowIfNull(isKeyDown);
        return modifier switch
        {
            HotCornerModifierKey.Ctrl => isKeyDown(VkControl),
            HotCornerModifierKey.Shift => isKeyDown(VkShift),
            HotCornerModifierKey.Alt => isKeyDown(VkMenu),
            _ => true
        };
    }

    private void HideCountdown()
    {
        if (_countdownVisible)
        {
            _countdownVisible = false;
            CountdownHidden?.Invoke();
        }
    }

    private static string Describe(ApplicationSettings settings, string actionId) =>
        DisplayText.ForDisplay(ActionCatalog.GetDescriptiveName(settings, actionId));
}
