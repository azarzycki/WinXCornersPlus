using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace WinXCorners.App.Tests.Infrastructure;

/// <summary>
/// Runs test code on a dedicated STA thread with a WinForms synchronization context, the way the app's UI thread
/// and action worker run. Exceptions (including Skip) are rethrown on the test thread; a hung body fails the test
/// after the timeout instead of hanging the CI job.
/// </summary>
internal static class Sta
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    internal static void Run(Action body, TimeSpan? timeout = null)
    {
        Run<object?>(() =>
        {
            body();
            return null;
        }, timeout);
    }

    internal static T Run<T>(Func<T> body, TimeSpan? timeout = null)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            using var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        })
        {
            IsBackground = true,
            Name = "Test STA thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var limit = timeout ?? DefaultTimeout;
        if (!thread.Join(limit))
        {
            throw new TimeoutException($"The STA test body did not finish within {limit.TotalSeconds:0} s.");
        }

        failure?.Throw();
        return result;
    }

    /// <summary>Pumps the current thread's messages until <paramref name="condition"/> holds or the time is up.</summary>
    internal static bool PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > timeout)
            {
                return false;
            }

            Application.DoEvents();
            Thread.Sleep(10);
        }

        return true;
    }

    /// <summary>Pumps the current thread's messages for <paramref name="duration"/>.</summary>
    internal static void Pump(TimeSpan duration)
    {
        PumpUntil(static () => false, duration);
    }
}
