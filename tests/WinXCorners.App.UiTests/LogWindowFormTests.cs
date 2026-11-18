using System.Diagnostics;
using System.Reflection;
using WinXCorners.App.UiTests.Infrastructure;
using WinXCorners.Core.Diagnostics;

namespace WinXCorners.App.UiTests;

public sealed class LogWindowFormTests
{
    /// <summary>Review B1: once the log window had been opened, Application.Exit() was vetoed and the process stayed.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplicationExit_EndsARealMessageLoop_WithinTwoSeconds(bool visible)
    {
        var log = new BoundedLog();
        log.Add("started");

        Ui.Run(() =>
        {
            var form = new LogWindowForm(log);
            form.Show();
            Ui.Pump(50);
            if (!visible)
            {
                form.Hide();
            }

            var (elapsed, vetoed) = MessageLoop.RunUntilExit(Application.Exit);
            Assert.False(vetoed, "Application.Exit() did not end the message loop (watchdog had to).");
            Assert.True(elapsed < TimeSpan.FromSeconds(2), $"The loop took {elapsed.TotalMilliseconds:0} ms to end.");
            form.Dispose();
        });
    }

    [Fact]
    public void ClosingByTheUser_OnlyHides()
    {
        Ui.Run(() =>
        {
            using var form = new LogWindowForm(new BoundedLog());
            form.Show();
            Ui.Pump(50);
            form.Close();
            Ui.Pump(50);
            Assert.False(form.IsDisposed);
            Assert.False(form.Visible);

            Find.Control<Button>(form, "closeButton").PerformClick(); // hidden already: still fine
            form.Show();
            Ui.Pump(50);
            Find.Control<Button>(form, "closeButton").PerformClick();
            Assert.False(form.Visible);
            Assert.False(form.IsDisposed);
        });
    }

    [Fact]
    public void MoreThan2000Lines_AreTrimmedToTheLast1000()
    {
        Ui.Run(() =>
        {
            using var form = new LogWindowForm(new BoundedLog());
            form.Show();
            for (var i = 1; i <= LogWindowForm.MaxLines + 1; i++)
            {
                form.AppendLog("line " + i);
            }

            var box = Find.Control<TextBox>(form, "logTextBox");
            var lines = box.Text.Split(Environment.NewLine);
            Assert.Equal(LogWindowForm.TrimToLines, lines.Length);
            Assert.Equal("line " + (LogWindowForm.MaxLines + 1), lines[^1]);
            Assert.Equal("line " + (LogWindowForm.MaxLines + 2 - LogWindowForm.TrimToLines), lines[0]);
        });
    }

    [Fact]
    public void EntriesLoggedOnAnotherThread_Appear()
    {
        var log = new BoundedLog();
        Ui.Run(() =>
        {
            using var form = new LogWindowForm(log);
            form.Show();
            Ui.Pump(50);
            var worker = new Thread(() => log.Add("from the worker thread"));
            worker.Start();
            worker.Join();

            var box = Find.Control<TextBox>(form, "logTextBox");
            Assert.True(Ui.PumpUntil(() => box.Text.Contains("from the worker thread", StringComparison.Ordinal)),
                "The entry logged on another thread never appeared.");
        });
    }

    [Fact]
    public void Clear_EmptiesTheViewAndTheLog()
    {
        var log = new BoundedLog();
        log.Add("one");
        log.Add("two");
        Ui.Run(() =>
        {
            using var form = new LogWindowForm(log);
            form.Show();
            Ui.Pump(50);
            Find.Control<Button>(form, "clearButton").PerformClick();
            Assert.Equal(string.Empty, Find.Control<TextBox>(form, "logTextBox").Text);
            Assert.Empty(log.Snapshot());
        });
    }

    [Fact]
    public void Dispose_LeavesNoHandlerSubscribedToTheLog()
    {
        var log = new BoundedLog();
        Ui.Run(() =>
        {
            var form = new LogWindowForm(log);
            form.Show();
            Ui.Pump(50);
            Assert.NotNull(EntryAddedHandlers(log));
            form.Dispose();
        });

        Assert.Null(EntryAddedHandlers(log));
        log.Add("after dispose"); // must not throw
    }

    [Theory]
    [InlineData(Theme.Light)]
    [InlineData(Theme.Dark)]
    public void Screenshot_WithEntries(Theme theme)
    {
        using var themeScope = ThemeScope.Use(theme);
        var log = new BoundedLog();
        log.Add("WinXCornersPlus 1.5.0 started.");
        log.Add(@"Settings: Loaded (C:\Users\Test User\AppData\Roaming\WinXCornersPlus\settings.json).");
        log.Add("Running 'File Explorer' from the TopLeft corner.");
        log.Add("'Task View' not run: a full-screen window is in front.");
        log.Add("Hot corners disabled.");

        Ui.Run(() =>
        {
            using var form = new LogWindowForm(log);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = Screenshot.DefaultLocation;
            form.Show();
            Ui.Pump(100);
            LayoutAssert.IsSound(form);
            Screenshot.Capture(form, $"log-{theme.ToString().ToLowerInvariant()}");
        });
    }

    private static Delegate? EntryAddedHandlers(BoundedLog log) =>
        (Delegate?)typeof(BoundedLog).GetField(nameof(BoundedLog.EntryAdded), BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(log);
}

/// <summary>Runs a real message loop on the current (STA) thread and measures how long an exit request takes to end it.</summary>
internal static class MessageLoop
{
    /// <param name="requestExit">Called from inside the loop, about 100 ms after it starts.</param>
    /// <param name="context">The loop's context; a new one when null.</param>
    /// <returns>Time from the request to the end of the loop, and whether the 5 s watchdog had to end it.</returns>
    internal static (TimeSpan Elapsed, bool Vetoed) RunUntilExit(Action requestExit, ApplicationContext? context = null)
    {
        using var ownContext = context is null ? new ApplicationContext() : null;
        var loopContext = context ?? ownContext!;
        var clock = new Stopwatch();
        var vetoed = false;

        using var start = new System.Windows.Forms.Timer { Interval = 100 };
        start.Tick += (_, _) =>
        {
            start.Stop();
            clock.Start();
            requestExit();
        };

        using var watchdog = new System.Windows.Forms.Timer { Interval = 5000 };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            vetoed = true;
            loopContext.ExitThread();
        };

        start.Start();
        watchdog.Start();
        Application.Run(loopContext);
        clock.Stop();
        start.Stop();
        watchdog.Stop();
        return (clock.Elapsed, vetoed);
    }
}
