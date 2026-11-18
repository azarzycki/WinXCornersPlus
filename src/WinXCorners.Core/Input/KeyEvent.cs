namespace WinXCorners.Core.Input;

/// <summary>
/// One keyboard event for the app to inject with <c>SendInput</c>, followed by a pause.
/// </summary>
/// <param name="VirtualKey">The virtual-key code.</param>
/// <param name="KeyUp">True for KEYEVENTF_KEYUP.</param>
/// <param name="Extended">True when KEYEVENTF_EXTENDEDKEY must be set.</param>
/// <param name="DelayAfterMs">Milliseconds to wait after sending this event before the next one.</param>
public readonly record struct KeyEvent(ushort VirtualKey, bool KeyUp, bool Extended, int DelayAfterMs);
