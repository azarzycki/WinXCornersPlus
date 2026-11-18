using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>
/// Runs UI test bodies on a dedicated STA thread with a WindowsFormsSynchronizationContext, pumps messages in bounded
/// loops, rethrows failures on the test thread and enforces a timeout, so a stuck modal window fails the test instead
/// of hanging the job. After every body it checks that no window was left open.
/// </summary>
internal static class Ui
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    private const int WmClose = 0x0010;

    /// <summary>WinForms defaults the app sets in Program.Main, applied once before any window exists.</summary>
    [ModuleInitializer]
    internal static void InitializeWinForms()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // An exception in an event handler must reach the test, not the default "unhandled exception" dialog,
        // which would block the run.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
    }

    /// <summary>Runs <paramref name="body"/> on a new STA thread and fails if any window is still open afterwards.</summary>
    internal static void Run(Action body, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        var threadId = 0;

        var thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            var context = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                var leftovers = CloseLeftovers();
                if (failure is null && leftovers.Count > 0)
                {
                    failure = new InvalidOperationException("Windows left open by the test: " + string.Join(", ", leftovers));
                }
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
                context.Dispose();
                done.Set();
            }
        })
        {
            IsBackground = true,
            Name = "UI test STA thread"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        if (!done.Wait(timeout ?? DefaultTimeout))
        {
            // Most likely a modal loop that nobody closes. Ask every window of the thread to close so the thread can
            // finish, and fail the test either way.
            if (threadId != 0)
            {
                EnumThreadWindows(threadId, (hwnd, _) => PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero), IntPtr.Zero);
            }

            done.Wait(TimeSpan.FromSeconds(5));
            throw new TimeoutException($"The UI test did not finish within {(timeout ?? DefaultTimeout).TotalSeconds:0} s (a modal window may have been left open).");
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Processes pending messages for about <paramref name="milliseconds"/>.</summary>
    internal static void Pump(int milliseconds = 50)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        while (clock.ElapsedMilliseconds < milliseconds);

        Application.DoEvents();
    }

    /// <summary>Pumps until <paramref name="condition"/> holds or <paramref name="timeoutMs"/> passes; returns the condition.</summary>
    internal static bool PumpUntil(Func<bool> condition, int timeoutMs = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
            {
                return condition();
            }

            Application.DoEvents();
            Thread.Sleep(5);
        }

        return true;
    }

    /// <summary>
    /// Posts a key press to <paramref name="target"/> and pumps, so it goes through the message loop's dialog-key
    /// processing (Enter, Esc) like a real key press.
    /// </summary>
    internal static void PressKey(Control target, Keys key)
    {
        const int wmKeyDown = 0x0100;
        const int wmKeyUp = 0x0101;
        PostMessage(target.Handle, wmKeyDown, (IntPtr)(int)key, (IntPtr)1);
        PostMessage(target.Handle, wmKeyUp, (IntPtr)(int)key, unchecked((IntPtr)(int)0xC0000001));
        Pump(150);
    }

    /// <summary>Disposes every form still open and returns their names.</summary>
    private static List<string> CloseLeftovers()
    {
        var names = new List<string>();
        Application.DoEvents();
        foreach (var form in Application.OpenForms.Cast<Form>().ToList())
        {
            names.Add($"{form.Name} ({form.GetType().Name}, visible={form.Visible})");
            form.Dispose();
        }

        Application.DoEvents();
        return names;
    }

    private delegate bool EnumThreadWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(int threadId, EnumThreadWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
