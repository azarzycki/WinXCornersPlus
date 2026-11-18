using System.ComponentModel;
using System.Runtime.InteropServices;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Input;

namespace WinXCorners.App;

/// <summary>
/// Injects the keyboard events Core's <see cref="KeyEventPlanner"/> planned, with <c>SendInput</c>, and polls the
/// physical modifier keys. Runs on the action worker thread: <see cref="Send"/> sleeps between events.
/// </summary>
internal sealed class KeySender : IKeySender
{
    private static readonly int InputSize = Marshal.SizeOf<NativeMethods.INPUT>();

    private readonly BoundedLog _log;

    internal KeySender(BoundedLog log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Sends the events one at a time, pausing <see cref="KeyEvent.DelayAfterMs"/> after each. Events are sent even
    /// after one is rejected, so the key-ups that end the sequence are always attempted (no stuck keys).
    /// </summary>
    public void Send(IReadOnlyList<KeyEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var rejected = 0;
        var lastError = 0;
        var buffer = new NativeMethods.INPUT[1];
        foreach (var keyEvent in events)
        {
            buffer[0] = ToInput(keyEvent);
            if (NativeMethods.SendInput(1, buffer, InputSize) == 0)
            {
                rejected++;
                lastError = Marshal.GetLastPInvokeError();
            }

            if (keyEvent.DelayAfterMs > 0)
            {
                Thread.Sleep(keyEvent.DelayAfterMs);
            }
        }

        if (rejected > 0)
        {
            // UIPI blocks input aimed at a window of a process with higher rights than this one. Windows then
            // returns 0 with either no error code or ERROR_ACCESS_DENIED (5), depending on the build.
            const int ErrorAccessDenied = 5;
            var reason = lastError is 0 or ErrorAccessDenied
                ? "blocked, probably because the window in front runs as administrator and this app does not"
                : new Win32Exception(lastError).Message;
            _log.Add($"SendInput rejected {rejected} of {events.Count} key events: {reason}.");
        }
    }

    /// <summary>The side-specific modifier keys (<see cref="VirtualKeys.PhysicalModifierKeys"/>) that are down now.</summary>
    public IReadOnlyList<ushort> GetModifiersDown()
    {
        var down = new List<ushort>();
        foreach (var key in VirtualKeys.PhysicalModifierKeys)
        {
            if (IsKeyDown(key))
            {
                down.Add(key);
            }
        }

        return down;
    }

    public bool IsKeyDown(ushort virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & NativeMethods.KeyDownBit) != 0;
    }

    internal static NativeMethods.INPUT ToInput(KeyEvent keyEvent)
    {
        var flags = 0u;
        if (keyEvent.KeyUp)
        {
            flags |= NativeMethods.KEYEVENTF_KEYUP;
        }

        if (keyEvent.Extended)
        {
            flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;
        }

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = keyEvent.VirtualKey,
                    wScan = (ushort)NativeMethods.MapVirtualKey(keyEvent.VirtualKey, NativeMethods.MAPVK_VK_TO_VSC),
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }
}
