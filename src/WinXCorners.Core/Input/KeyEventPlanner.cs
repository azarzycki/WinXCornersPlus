namespace WinXCorners.Core.Input;

/// <summary>
/// Turns a <see cref="HotkeySequence"/> into the exact list of keyboard events to inject.
/// </summary>
public static class KeyEventPlanner
{
    /// <summary>Pause between events used by the original app; long enough for slow target windows.</summary>
    public const int DefaultDelayMs = 10;

    /// <summary>Upper bound on the per-event pause, so a bad setting cannot freeze the keyboard for long.</summary>
    public const int MaxDelayMs = 250;

    /// <summary>
    /// Plans the events for <paramref name="sequence"/>.
    /// </summary>
    /// <param name="sequence">The validated sequence.</param>
    /// <param name="modifiersCurrentlyDown">
    /// Modifier keys the user is physically holding (as polled by the app). Only values from
    /// <see cref="VirtualKeys.PhysicalModifierKeys"/> are honoured; others and duplicates are ignored.
    /// </param>
    /// <param name="delayMs">Pause after each event, clamped to 0..<see cref="MaxDelayMs"/>.</param>
    /// <remarks>
    /// <para>The user's held modifiers are released first so that, for example, a Ctrl held to arm the hot
    /// corner does not turn the sequence's <c>tab</c> into Ctrl+Tab.</para>
    /// <para>They are deliberately NOT pressed again afterwards. The original app re-pressed them, which left
    /// a logically stuck key whenever the user let go during the sequence (review B5): Windows then believed
    /// Ctrl was down until the user pressed and released it again. Not re-pressing is safe because a key that
    /// is still physically held re-asserts itself through keyboard auto-repeat.</para>
    /// <para>Every key the sequence holds and does not release is released at the end, in reverse order of
    /// holding, so no sequence can leave a key down.</para>
    /// </remarks>
    public static IReadOnlyList<KeyEvent> Plan(HotkeySequence sequence, IEnumerable<ushort> modifiersCurrentlyDown, int delayMs = DefaultDelayMs)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(modifiersCurrentlyDown);

        var delay = Math.Clamp(delayMs, 0, MaxDelayMs);
        var events = new List<KeyEvent>();
        void Emit(ushort virtualKey, bool keyUp) =>
            events.Add(new KeyEvent(virtualKey, keyUp, VirtualKeys.IsExtended(virtualKey), delay));

        // Release in the fixed PhysicalModifierKeys order: deterministic whatever order the caller polled in.
        var userModifiers = modifiersCurrentlyDown.ToHashSet();
        foreach (var modifier in VirtualKeys.PhysicalModifierKeys)
        {
            if (userModifiers.Contains(modifier))
            {
                Emit(modifier, keyUp: true);
            }
        }

        var held = new List<ushort>();
        foreach (var step in sequence.Steps)
        {
            switch (step.Kind)
            {
                case KeyStepKind.Hold:
                    Emit(step.VirtualKey, keyUp: false);
                    if (!held.Contains(step.VirtualKey))
                    {
                        held.Add(step.VirtualKey);
                    }

                    break;
                case KeyStepKind.Release:
                    // Releasing a key that is not held is legal: it simply sends a key-up.
                    Emit(step.VirtualKey, keyUp: true);
                    held.Remove(step.VirtualKey);
                    break;
                default:
                    Emit(step.VirtualKey, keyUp: false);
                    Emit(step.VirtualKey, keyUp: true);
                    // A press ends with the key up, even if an earlier token was holding it.
                    held.Remove(step.VirtualKey);
                    break;
            }
        }

        for (var index = held.Count - 1; index >= 0; index--)
        {
            Emit(held[index], keyUp: true);
        }

        // Nothing follows the last event, so waiting after it would only delay the caller.
        events[^1] = events[^1] with { DelayAfterMs = 0 };
        return events.AsReadOnly();
    }

    /// <summary>Total time the events take to send: the sum of their <see cref="KeyEvent.DelayAfterMs"/>.</summary>
    public static int TotalDurationMs(IReadOnlyList<KeyEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var total = 0;
        foreach (var keyEvent in events)
        {
            total += keyEvent.DelayAfterMs;
        }

        return total;
    }
}
