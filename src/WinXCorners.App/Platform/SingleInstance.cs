using System.ComponentModel;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace WinXCorners.App;

/// <summary>
/// Makes sure one copy of the app runs per user session, and lets a second launch ask the running copy to show
/// its Settings window (brief design point 4: a hidden tray icon can never lock the user out).
/// </summary>
/// <remarks>
/// <para>Threading: <see cref="TryAcquire"/>, <see cref="Release"/> and <see cref="Dispose"/> must all be called on
/// the main (UI) thread. A mutex is owned by the thread that acquired it and only that thread can release it.</para>
/// <para>Why explicit ACLs (review B7/S4): an elevated process creates kernel objects with a default DACL that
/// grants only Administrators and SYSTEM, so a normal-rights copy of the same user got
/// UnauthorizedAccessException instead of noticing the running instance. Both objects are therefore created with
/// a DACL that grants the current user's SID access, which an elevated and a normal process of that user share.
/// No other account is granted anything. The user gets full control rather than only synchronize+modify because
/// MutexAcl.Create / EventWaitHandleAcl.Create open an already existing object asking for all access; a narrower
/// grant would make the second instance fail to open it. The user already has full control over objects created
/// with a normal-rights default DACL, so this grants nothing new.</para>
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    internal const string DefaultMutexName = @"Local\WinXCornersPlus.SingleInstance";
    internal const string DefaultActivationEventName = @"Local\WinXCornersPlus.Activate";

    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationWait;

    private SingleInstance(Mutex mutex, EventWaitHandle? activationEvent)
    {
        _mutex = mutex;
        _activationEvent = activationEvent;
        if (activationEvent is not null)
        {
            _activationWait = ThreadPool.RegisterWaitForSingleObject(
                activationEvent, OnActivationSignalled, state: null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    /// <summary>
    /// Raised when another launch called <see cref="SignalExistingInstance"/>. Raised on a thread-pool thread:
    /// subscribers marshal to the UI thread themselves.
    /// </summary>
    public event Action? ActivationRequested;

    /// <summary>
    /// Takes the single-instance mutex. Returns null when another instance holds it (or its objects cannot be
    /// opened, which also means another instance exists); never throws for access problems.
    /// </summary>
    /// <param name="waitForProcessId">
    /// The process that started this one with <c>--restarted-from</c>: wait for it to exit (up to
    /// <paramref name="timeout"/>), then keep trying for the mutex until the timeout. Without it the mutex is tried
    /// once, immediately, so a second launch signals the running copy without delay.
    /// </param>
    /// <param name="timeout">Total time allowed for both waits.</param>
    /// <param name="nameSuffix">Appended to both object names; tests use a unique suffix.</param>
    public static SingleInstance? TryAcquire(int? waitForProcessId, TimeSpan timeout, string? nameSuffix = null)
    {
        if (timeout < TimeSpan.Zero)
        {
            timeout = TimeSpan.Zero;
        }

        var clock = Stopwatch.StartNew();
        if (waitForProcessId is int processId)
        {
            WaitForExit(processId, timeout);
        }

        var mutexWait = waitForProcessId is null ? TimeSpan.Zero : Remaining(timeout, clock.Elapsed);
        var mutex = CreateMutex(DefaultMutexName + nameSuffix);
        if (mutex is null)
        {
            return null;
        }

        if (!WaitForMutex(mutex, mutexWait))
        {
            mutex.Dispose();
            return null;
        }

        // Without the event the app still works; only "second launch opens Settings" is lost.
        var activationEvent = CreateActivationEvent(DefaultActivationEventName + nameSuffix);
        return new SingleInstance(mutex, activationEvent);
    }

    /// <summary>Wakes the running instance (it opens its Settings window). False when none could be reached.</summary>
    public static bool SignalExistingInstance(string? nameSuffix = null)
    {
        try
        {
            if (!EventWaitHandleAcl.TryOpenExisting(
                    DefaultActivationEventName + nameSuffix,
                    EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                    out var existing))
            {
                return false;
            }

            using (existing)
            {
                return existing.Set();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Releases the mutex and closes the activation event now, so a replacement process can take over while this
    /// one shuts down. Idempotent. Main thread only.
    /// </summary>
    public void Release()
    {
        var wait = Interlocked.Exchange(ref _activationWait, null);
        wait?.Unregister(null);

        var activationEvent = Interlocked.Exchange(ref _activationEvent, null);
        activationEvent?.Dispose();

        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Called on a thread that does not own the mutex (a caller bug). Closing the handle below still
            // lets the next owner in: Windows then hands it over as abandoned, which TryAcquire accepts.
        }
        finally
        {
            mutex.Dispose();
        }
    }

    public void Dispose() => Release();

    private void OnActivationSignalled(object? state, bool timedOut)
    {
        if (!timedOut)
        {
            ActivationRequested?.Invoke();
        }
    }

    private static TimeSpan Remaining(TimeSpan timeout, TimeSpan elapsed)
    {
        var remaining = timeout - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static void WaitForExit(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.WaitForExit(timeout);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Exited while we looked.
        }
        catch (Win32Exception)
        {
            // Not allowed to wait on it (e.g. it was elevated and we are not): fall back to waiting for the mutex.
        }
        catch (SystemException ex) when (ex is NotSupportedException or PlatformNotSupportedException)
        {
            // Same fallback.
        }
    }

    private static bool WaitForMutex(Mutex mutex, TimeSpan wait)
    {
        try
        {
            return mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // The previous owner ended without releasing it (crash or kill): we own it now.
            return true;
        }
    }

    private static Mutex? CreateMutex(string name)
    {
        try
        {
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(CurrentUser(), MutexRights.FullControl, AccessControlType.Allow));
            return MutexAcl.Create(initiallyOwned: false, name, out _, security);
        }
        catch (UnauthorizedAccessException)
        {
            return null; // Exists but not ours to open: treat as "another instance is running".
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return null; // The name is taken by a different kind of object.
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static EventWaitHandle? CreateActivationEvent(string name)
    {
        try
        {
            var security = new EventWaitHandleSecurity();
            security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser(), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            return EventWaitHandleAcl.Create(initialState: false, EventResetMode.AutoReset, name, out _, security);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new UnauthorizedAccessException("The current user has no SID.");
    }
}
