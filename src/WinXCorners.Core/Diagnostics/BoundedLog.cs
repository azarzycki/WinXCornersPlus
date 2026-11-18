using System.Globalization;
using System.Text;

namespace WinXCorners.Core.Diagnostics;

/// <summary>
/// In-memory runtime log shown in the Log window. Keeps the newest <see cref="Capacity"/> entries.
/// Thread-safe: actions and the input path log from different threads.
/// </summary>
public sealed class BoundedLog
{
    private readonly Queue<string> _entries;
    private readonly Lock _sync = new();
    private readonly TimeProvider _timeProvider;

    /// <param name="capacity">Maximum number of entries kept; must be positive.</param>
    /// <param name="timeProvider">Source of the local timestamp; defaults to the system clock.</param>
    public BoundedLog(int capacity = 500, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        Capacity = capacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _entries = new Queue<string>(Math.Min(capacity, 1024));
    }

    /// <summary>
    /// Raised after an entry is stored, with the formatted entry. Raised outside the lock, possibly on
    /// the logging thread; handlers must marshal to the UI thread themselves.
    /// </summary>
    public event Action<string>? EntryAdded;

    public int Capacity { get; }

    /// <summary>
    /// Appends <c>"[yyyy-MM-dd HH:mm:ss.fff] message"</c> (local time). Line breaks and other control
    /// characters are replaced by spaces: messages contain text from settings.json (command lines), and
    /// a newline in it must not be able to forge what looks like a separate log entry.
    /// </summary>
    public void Add(string message)
    {
        var timestamp = _timeProvider.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var entry = $"[{timestamp}] {Neutralize(message)}";

        lock (_sync)
        {
            while (_entries.Count >= Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(entry);
        }

        EntryAdded?.Invoke(entry);
    }

    /// <summary>A copy of the entries, oldest first.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _entries.Clear();
        }
    }

    private static string Neutralize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        StringBuilder? builder = null;
        for (var i = 0; i < message.Length; i++)
        {
            if (IsUnsafe(message[i]))
            {
                builder ??= new StringBuilder(message);
                builder[i] = ' ';
            }
        }

        return builder?.ToString() ?? message;
    }

    // Control characters (C0, DEL, C1 incl. NEL) plus the Unicode line/paragraph separators, which a
    // text box may also render as a line break.
    private static bool IsUnsafe(char c)
    {
        return char.IsControl(c) || c is '\u2028' or '\u2029';
    }
}
