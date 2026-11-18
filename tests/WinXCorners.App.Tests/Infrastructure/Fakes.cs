using System.Collections.Concurrent;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Input;
using WinXCorners.Core.Security;

namespace WinXCorners.App.Tests.Infrastructure;

internal sealed class FakeKeySender : IKeySender
{
    internal ConcurrentQueue<IReadOnlyList<KeyEvent>> Sent { get; } = new();

    internal ConcurrentQueue<Thread> Threads { get; } = new();

    internal IReadOnlyList<ushort> ModifiersDown { get; set; } = [];

    public void Send(IReadOnlyList<KeyEvent> events)
    {
        Threads.Enqueue(Thread.CurrentThread);
        Sent.Enqueue(events.ToArray());
    }

    public IReadOnlyList<ushort> GetModifiersDown() => ModifiersDown;
}

internal sealed class FakeLauncher : IProcessLauncher
{
    internal ConcurrentQueue<LaunchRequest> Requests { get; } = new();

    internal ConcurrentQueue<Thread> Threads { get; } = new();

    /// <summary>When set, each launch waits for it (up to 30 s) after signalling <see cref="Entered"/>.</summary>
    internal ManualResetEventSlim? Gate { get; set; }

    internal SemaphoreSlim Entered { get; } = new(0);

    /// <summary>Thrown by the next launch (once).</summary>
    internal Exception? ThrowNext;

    internal bool Result { get; set; } = true;

    public bool TryLaunch(LaunchRequest request, out string? error)
    {
        Threads.Enqueue(Thread.CurrentThread);
        Requests.Enqueue(request);
        Entered.Release();
        Gate?.Wait(TimeSpan.FromSeconds(30));
        var toThrow = Interlocked.Exchange(ref ThrowNext, null);
        if (toThrow is not null)
        {
            throw toThrow;
        }

        error = Result ? null : "fake launch failure";
        return Result;
    }
}

internal sealed class FakeWindowQuery : IWindowQuery
{
    internal WindowInfo? Foreground { get; set; }

    internal HashSet<(string? ClassName, string? Title)> Existing { get; } = [];

    internal ConcurrentQueue<string> Calls { get; } = new();

    public WindowInfo? GetForegroundWindow()
    {
        Calls.Enqueue("foreground");
        return Foreground;
    }

    public bool WindowExists(string? className, string? title)
    {
        Calls.Enqueue($"exists({className ?? "<null>"},{title ?? "<null>"})");
        return Existing.Contains((className, title));
    }
}

internal sealed class FakeSystemActions : ISystemActions
{
    internal ConcurrentQueue<(IntPtr Window, SystemCommandKind Kind)> Commands { get; } = new();

    internal int Locks;

    public bool TryPostSystemCommand(IntPtr window, SystemCommandKind kind, out string? error)
    {
        Commands.Enqueue((window, kind));
        error = null;
        return true;
    }

    public bool TryLockWorkstation(out string? error)
    {
        Interlocked.Increment(ref Locks);
        error = null;
        return true;
    }
}

/// <summary>An in-memory <see cref="IApprovedSettingsStore"/>.</summary>
internal sealed class MemoryApprovalStore : IApprovedSettingsStore
{
    internal string? Value { get; set; }

    internal bool Writable { get; set; } = true;

    internal int Writes { get; private set; }

    public string? Read() => Value;

    public bool TryWrite(string approval)
    {
        if (!Writable)
        {
            return false;
        }

        Writes++;
        Value = approval;
        return true;
    }
}
