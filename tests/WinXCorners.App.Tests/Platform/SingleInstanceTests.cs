using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using WinXCorners.App.Tests.Infrastructure;

namespace WinXCorners.App.Tests.Platform;

/// <summary>
/// SingleInstance with real named kernel objects. Every test uses a unique name suffix, so the real app's objects
/// are never touched. A mutex belongs to the thread that acquired it, so "another instance" is played by another thread.
/// </summary>
public sealed class SingleInstanceTests
{
    private static string NewSuffix() => ".Test." + Guid.NewGuid().ToString("N");

    /// <summary>Runs <paramref name="body"/> on a fresh thread and waits for it (a different mutex owner).</summary>
    private static T OnOtherThread<T>(Func<T> body)
    {
        T result = default!;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The other thread hung.");
        if (failure is not null)
        {
            throw new InvalidOperationException("The other thread failed.", failure);
        }

        return result;
    }

    [Fact]
    public void Acquire_then_a_second_acquire_elsewhere_fails_until_released()
    {
        var suffix = NewSuffix();
        var first = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        Assert.NotNull(first);
        try
        {
            var second = OnOtherThread(() =>
            {
                var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
                instance?.Dispose();
                return instance;
            });
            Assert.Null(second);
        }
        finally
        {
            first.Release();
        }

        var third = OnOtherThread(() =>
        {
            var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
            var acquired = instance is not null;
            instance?.Release();
            return acquired;
        });
        Assert.True(third, "Release must let the next instance in.");
    }

    [Fact]
    public void Release_is_idempotent_and_Dispose_after_Release_does_not_throw()
    {
        var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, NewSuffix());
        Assert.NotNull(instance);
        instance.Release();
        instance.Release();
        instance.Dispose();
    }

    [Fact]
    public void Signal_raises_ActivationRequested_on_the_running_instance()
    {
        var suffix = NewSuffix();
        using var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        Assert.NotNull(instance);
        using var raised = new SemaphoreSlim(0);
        instance.ActivationRequested += () => raised.Release();

        Assert.True(SingleInstance.SignalExistingInstance(suffix));
        Assert.True(raised.Wait(TimeSpan.FromSeconds(10)), "ActivationRequested was not raised.");

        // The event is auto-reset and the wait is re-armed: a second launch is noticed too.
        Assert.True(SingleInstance.SignalExistingInstance(suffix));
        Assert.True(raised.Wait(TimeSpan.FromSeconds(10)), "The second activation was not raised.");
    }

    [Fact]
    public void Signal_without_a_running_instance_returns_false()
    {
        Assert.False(SingleInstance.SignalExistingInstance(NewSuffix()));
    }

    [Fact]
    public void After_Release_a_signal_no_longer_reaches_the_old_instance()
    {
        var suffix = NewSuffix();
        var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        Assert.NotNull(instance);
        var raised = 0;
        instance.ActivationRequested += () => Interlocked.Increment(ref raised);
        instance.Release();

        Assert.False(SingleInstance.SignalExistingInstance(suffix));
        Thread.Sleep(200);
        Assert.Equal(0, Volatile.Read(ref raised));
    }

    [Fact]
    public void An_abandoned_mutex_is_accepted_by_the_next_instance()
    {
        var suffix = NewSuffix();

        // The owner thread ends without releasing: Windows marks the mutex abandoned.
        var abandoned = OnOtherThread(() => SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix));
        Assert.NotNull(abandoned);
        try
        {
            var next = OnOtherThread(() =>
            {
                var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
                var acquired = instance is not null;
                instance?.Release();
                return acquired;
            });
            Assert.True(next, "An abandoned mutex must be taken over, not treated as a running instance.");
        }
        finally
        {
            abandoned.Dispose(); // Not the owner thread: Release closes the handle without throwing.
        }
    }

    [Fact]
    public void Restarted_from_waits_for_the_previous_process_to_exit()
    {
        // A real short-lived process plays the copy that is restarting us.
        using var previous = StartSleeper(seconds: 3);
        var clock = Stopwatch.StartNew();
        var instance = SingleInstance.TryAcquire(previous.Id, TimeSpan.FromSeconds(20), NewSuffix());
        clock.Stop();
        try
        {
            Assert.NotNull(instance);
            Assert.True(previous.HasExited, "TryAcquire returned before the previous process exited.");
            Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(1.5), $"Returned after only {clock.Elapsed}.");
        }
        finally
        {
            instance?.Release();
        }
    }

    [Fact]
    public void Restarted_from_an_unknown_process_id_acquires_at_once()
    {
        var clock = Stopwatch.StartNew();
        var instance = SingleInstance.TryAcquire(int.MaxValue - 7, TimeSpan.FromSeconds(10), NewSuffix());
        clock.Stop();
        Assert.NotNull(instance);
        instance.Release();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Took {clock.Elapsed}.");
    }

    [Fact]
    public void Restarted_from_honours_the_timeout_when_the_previous_process_keeps_running_and_holds_the_mutex()
    {
        var suffix = NewSuffix();
        using var holderReady = new ManualResetEventSlim();
        using var holderStop = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var held = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
            holderReady.Set();
            holderStop.Wait(TimeSpan.FromSeconds(60));
        }) { IsBackground = true };
        holder.Start();
        Assert.True(holderReady.Wait(TimeSpan.FromSeconds(10)));

        using var previous = StartSleeper(seconds: 60);
        try
        {
            var clock = Stopwatch.StartNew();
            var instance = SingleInstance.TryAcquire(previous.Id, TimeSpan.FromSeconds(2), suffix);
            clock.Stop();
            instance?.Release();

            Assert.Null(instance);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8));
        }
        finally
        {
            holderStop.Set();
            holder.Join(TimeSpan.FromSeconds(10));
            KillQuietly(previous);
        }
    }

    [Fact]
    public void A_negative_timeout_is_treated_as_zero()
    {
        var instance = SingleInstance.TryAcquire(null, TimeSpan.FromSeconds(-5), NewSuffix());
        Assert.NotNull(instance);
        instance.Release();
    }

    [Fact]
    public void A_mutex_name_taken_by_another_kind_of_object_means_another_instance_and_does_not_throw()
    {
        var suffix = NewSuffix();
        using var squatter = new Semaphore(1, 1, SingleInstance.DefaultMutexName + suffix);

        var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);

        Assert.Null(instance);
    }

    [Fact]
    public void An_activation_event_name_taken_by_another_object_still_lets_the_instance_run()
    {
        var suffix = NewSuffix();
        using var squatter = new Mutex(false, SingleInstance.DefaultActivationEventName + suffix);

        var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        try
        {
            Assert.NotNull(instance);
            Assert.False(SingleInstance.SignalExistingInstance(suffix));
        }
        finally
        {
            instance?.Release();
        }
    }

    [Fact]
    public void Both_objects_grant_only_the_current_user_full_control_and_can_be_opened_again()
    {
        var suffix = NewSuffix();
        using var instance = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
        Assert.NotNull(instance);
        var user = WindowsIdentity.GetCurrent().User!;

        using (var mutex = MutexAcl.OpenExisting(SingleInstance.DefaultMutexName + suffix, MutexRights.ReadPermissions | MutexRights.Synchronize))
        {
            var rules = mutex.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<MutexAccessRule>().ToList();
            var rule = Assert.Single(rules);
            Assert.Equal(user, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(MutexRights.FullControl, rule.MutexRights & MutexRights.FullControl);
        }

        using (var activation = EventWaitHandleAcl.OpenExisting(
                   SingleInstance.DefaultActivationEventName + suffix, EventWaitHandleRights.ReadPermissions | EventWaitHandleRights.Synchronize))
        {
            var rules = activation.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<EventWaitHandleAccessRule>().ToList();
            var rule = Assert.Single(rules);
            Assert.Equal(user, rule.IdentityReference);
        }

        // The second launch opens the existing objects through the same calls the app uses.
        var second = OnOtherThread(() =>
        {
            var other = SingleInstance.TryAcquire(null, TimeSpan.Zero, suffix);
            other?.Release();
            return other is null;
        });
        Assert.True(second);
        Assert.True(SingleInstance.SignalExistingInstance(suffix));
    }

    /// <summary>
    /// Review question (W5): would "synchronize + modify" be enough? MutexAcl.Create opens an existing object asking
    /// for full access, so with the narrower grant the second launch could not open it. This pins that down on
    /// real Windows, which is why the product grants full control (to the current user only).
    /// </summary>
    [Fact]
    public void A_narrower_grant_would_make_the_second_open_fail_which_is_why_full_control_is_granted()
    {
        var name = SingleInstance.DefaultMutexName + NewSuffix();
        var user = WindowsIdentity.GetCurrent().User!;
        var narrow = new MutexSecurity();
        narrow.AddAccessRule(new MutexAccessRule(user, MutexRights.Synchronize | MutexRights.Modify, AccessControlType.Allow));
        using var first = MutexAcl.Create(false, name, out var createdNew, narrow);
        Assert.True(createdNew);

        var full = new MutexSecurity();
        full.AddAccessRule(new MutexAccessRule(user, MutexRights.FullControl, AccessControlType.Allow));
        Assert.Throws<UnauthorizedAccessException>(() => MutexAcl.Create(false, name, out _, full).Dispose());
    }

    private static Process StartSleeper(int seconds)
    {
        // ping waits about a second between echoes; it is on every Windows image and needs no window.
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "PING.EXE"), $"-n {seconds + 1} 127.0.0.1")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Environment.SystemDirectory
        };
        var process = Process.Start(startInfo)!;
        process.OutputDataReceived += static (_, _) => { };
        process.BeginOutputReadLine();
        return process;
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }
}
