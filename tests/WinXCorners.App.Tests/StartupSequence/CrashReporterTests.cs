using WinXCorners.App.Tests.Infrastructure;
using WinXCorners.Core.Diagnostics;

namespace WinXCorners.App.Tests.StartupSequence;

/// <summary>
/// Installs the real global handlers. Application.SetUnhandledExceptionMode refuses once any window handle exists in
/// the process, which is why Program.Main installs the reporter first and why this collection runs first.
/// </summary>
[Collection(RunsFirstCollection.Name)]
public sealed class CrashReporterInstallTests
{
    [Fact]
    public void Installed_handlers_route_UI_thread_exceptions_to_the_crash_log_log_and_one_notification()
    {
        using var temp = new TempDirectory();
        var crashLog = Path.Combine(temp.Path, "nested", "crash.log");
        var reporter = new CrashReporter(crashLog);
        var log = new BoundedLog();
        var notifications = new List<string>();
        reporter.AttachLog(log);
        reporter.AttachNotifier(notifications.Add);

        reporter.Install();
        try
        {
            reporter.Install(); // Idempotent: one handler, so one entry per exception.
            Application.OnThreadException(new InvalidOperationException("first failure"));
            Application.OnThreadException(new FormatException("second failure"));
        }
        finally
        {
            reporter.Uninstall();
            reporter.Uninstall();
        }

        Application.OnThreadException(new InvalidOperationException("after uninstall"));

        var text = File.ReadAllText(crashLog);
        Assert.Contains("first failure", text, StringComparison.Ordinal);
        Assert.Contains("second failure", text, StringComparison.Ordinal);
        Assert.DoesNotContain("after uninstall", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("[UI thread]").Length - 1);
        Assert.Equal([CrashReporter.NotificationText], notifications);
        Assert.Equal(2, log.Snapshot().Count(static l => l.StartsWith("Unexpected error (UI thread)", StringComparison.Ordinal)));
    }
}

public sealed class CrashReporterTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Background_exceptions_are_written_with_their_origin()
    {
        var reporter = new CrashReporter(_temp.File("crash.log"));

        reporter.ReportFatal(new IOException("disk"), isTerminating: true);
        reporter.ReportFatal(null, isTerminating: false);

        var text = File.ReadAllText(_temp.File("crash.log"));
        Assert.Contains("[background thread, process ending] System.IO.IOException: disk", text, StringComparison.Ordinal);
        Assert.Contains("[background thread] (no exception object)", text, StringComparison.Ordinal);
        Assert.Null(reporter.LastCrashLogError);
    }

    [Fact]
    public void Without_a_notifier_nothing_is_shown_and_the_log_is_still_written()
    {
        var reporter = new CrashReporter(_temp.File("crash.log"));
        reporter.ReportUiException(new InvalidOperationException("x"));
        Assert.True(File.Exists(_temp.File("crash.log")));
    }

    [Fact]
    public void A_failing_notifier_does_not_throw()
    {
        var reporter = new CrashReporter(_temp.File("crash.log"));
        reporter.AttachNotifier(_ => throw new ObjectDisposedException("tray"));

        reporter.ReportUiException(new InvalidOperationException("x"));

        Assert.Contains("[notification]", File.ReadAllText(_temp.File("crash.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_crash_log_starts_over_past_its_size_limit()
    {
        var path = _temp.File("crash.log");
        File.WriteAllText(path, new string('x', (int)CrashReporter.MaxCrashLogBytes + 10));
        var reporter = new CrashReporter(path);

        reporter.ReportUiException(new InvalidOperationException("fresh"));

        var info = new FileInfo(path);
        Assert.True(info.Length < 10_000);
        Assert.Contains("fresh", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void A_crash_log_that_cannot_be_written_is_remembered_not_thrown()
    {
        var path = _temp.File("crash.log");
        using var locked = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var reporter = new CrashReporter(path);

        reporter.ReportUiException(new InvalidOperationException("x"));

        Assert.NotNull(reporter.LastCrashLogError);
    }

    [Fact]
    public void The_default_crash_log_lives_in_local_app_data()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinXCornersPlus", "crash.log"),
            CrashReporter.DefaultCrashLogPath());
    }
}
