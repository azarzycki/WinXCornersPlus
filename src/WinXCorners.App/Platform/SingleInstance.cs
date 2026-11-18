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
/// <para>Why explicit ACLs (review B7/S4, V2 D6): an elevated process creates kernel objects with a default DACL
/// that grants only Administrators and SYSTEM, so a normal-rights copy of the same user got
/// UnauthorizedAccessException instead of noticing the running instance. Both objects are therefore created with a
/// DACL that grants the current user's SID exactly what a second launch needs (SYNCHRONIZE to wait, MODIFY_STATE to
/// release the mutex or set the event), an OWNER RIGHTS entry with the same rights so that whoever owns the object
/// cannot rewrite its DACL either (without it the owner implicitly has WRITE_DAC), and full control for SYSTEM and
/// Administrators. Nobody running with normal rights can lock the objects (WRITE_DAC, WRITE_OWNER) any more.</para>
/// <para>An existing object is opened first, asking for only those rights; only when none exists is one created.
/// MutexAcl.Create on its own would open an existing object asking for full access, which the narrow DACL refuses.</para>
/// </remarks>
internal sealed class SingleInstance : IDisposable
{
    internal const string DefaultMutexName = @"Local\WinXCornersPlus.SingleInstance";
    internal const string DefaultActivationEventName = @"Local\WinXCornersPlus.Activate";

    /// <summary>Upper bound for an activation callback still running when Release is called.</summary>
    private static readonly TimeSpan UnregisterTimeout = TimeSpan.FromSeconds(2);

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
        if (wait is not null)
        {
            // Unregister(null) returns at once while the thread pool still holds the event handle, so the named
            // event outlived Release and a launch in that window "signalled" a copy that no longer listens (found on
            // Windows CI). Waiting for the unregistration also guarantees no ActivationRequested after Release.
            using var unregistered = new ManualResetEvent(false);
            if (wait.Unregister(unregistered))
            {
                unregistered.WaitOne(UnregisterTimeout);
            }
        }

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

    /// <summary>
    /// Process ids of other processes in this session whose image has this executable's name (at most a few): the
    /// candidates for the running copy, which a second launch lets take the foreground. A name is not proof, but the
    /// right is only to bring a window to the front once, and only for those processes rather than for any process.
    /// </summary>
    internal static IReadOnlyList<int> FindRunningCopies()
    {
        using var self = Process.GetCurrentProcess();
        var found = new List<int>();
        foreach (var process in Process.GetProcessesByName(self.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id != self.Id && process.SessionId == self.SessionId && found.Count < 4)
                    {
                        found.Add(process.Id);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Exited while we looked.
                }
                catch (Win32Exception)
                {
                    // Its session cannot be read: not a candidate.
                }
            }
        }

        return found;
    }

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

    /// <summary>What a second launch needs on the mutex: wait for it and release it.</summary>
    internal const MutexRights UserMutexRights = MutexRights.Synchronize | MutexRights.Modify;

    /// <summary>What a second launch needs on the activation event: set it (and the owner waits on it).</summary>
    internal const EventWaitHandleRights UserEventRights = EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify;

    /// <summary>OWNER RIGHTS (S-1-3-4): replaces the owner's implicit READ_CONTROL and WRITE_DAC.</summary>
    internal static readonly SecurityIdentifier OwnerRights = new("S-1-3-4");

    /// <summary>Open-then-create attempts, for the race where another launch creates the object in between.</summary>
    private const int CreateAttempts = 3;

    private static Mutex? CreateMutex(string name)
    {
        for (var attempt = 0; attempt < CreateAttempts; attempt++)
        {
            try
            {
                if (MutexAcl.TryOpenExisting(name, UserMutexRights, out var existing))
                {
                    return existing;
                }

                var security = new MutexSecurity();
                security.AddAccessRule(new MutexAccessRule(CurrentUser(), UserMutexRights, AccessControlType.Allow));
                security.AddAccessRule(new MutexAccessRule(OwnerRights, UserMutexRights, AccessControlType.Allow));
                security.AddAccessRule(new MutexAccessRule(LocalSystem, MutexRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new MutexAccessRule(Administrators, MutexRights.FullControl, AccessControlType.Allow));
                return MutexAcl.Create(initiallyOwned: false, name, out _, security);
            }
            catch (UnauthorizedAccessException)
            {
                // Either the existing object does not grant us even SYNCHRONIZE + MODIFY_STATE (someone else's, or
                // squatted with a hostile DACL), or it was created between our open and our create and Create asked
                // for more than our DACL grants. Try the limited open again; give up after a few rounds.
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

        return null; // Exists but not ours to open: treat as "another instance is running".
    }

    private static EventWaitHandle? CreateActivationEvent(string name)
    {
        for (var attempt = 0; attempt < CreateAttempts; attempt++)
        {
            try
            {
                if (EventWaitHandleAcl.TryOpenExisting(name, UserEventRights, out var existing))
                {
                    return existing;
                }

                var security = new EventWaitHandleSecurity();
                security.AddAccessRule(new EventWaitHandleAccessRule(CurrentUser(), UserEventRights, AccessControlType.Allow));
                security.AddAccessRule(new EventWaitHandleAccessRule(OwnerRights, UserEventRights, AccessControlType.Allow));
                security.AddAccessRule(new EventWaitHandleAccessRule(LocalSystem, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                security.AddAccessRule(new EventWaitHandleAccessRule(Administrators, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                return EventWaitHandleAcl.Create(initialState: false, EventResetMode.AutoReset, name, out _, security);
            }
            catch (UnauthorizedAccessException)
            {
                // As for the mutex: a hostile DACL, or a creation race; retry the limited open.
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

        return null;
    }

    private static SecurityIdentifier LocalSystem => new(WellKnownSidType.LocalSystemSid, null);

    private static SecurityIdentifier Administrators => new(WellKnownSidType.BuiltinAdministratorsSid, null);

    private static SecurityIdentifier CurrentUser()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new UnauthorizedAccessException("The current user has no SID.");
    }
}
