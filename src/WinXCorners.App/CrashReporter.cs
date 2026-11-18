using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Text;
using WinXCorners.Core.Diagnostics;

namespace WinXCorners.App;

/// <summary>
/// Global exception handling (review M3). An exception that escapes a UI event handler is logged, appended to
/// <c>%LOCALAPPDATA%\WinXCornersPlus\crash.log</c> and reported once in a non-modal notification, and the tray
/// app keeps running. An exception on another thread ends the process (the runtime decides that); it is still
/// written to crash.log first. Nothing here ever throws. An elevated process passes no path: it writes no file at all
/// (review V2 D1), and the in-app log keeps the summary.
/// </summary>
internal sealed class CrashReporter
{
    /// <summary>crash.log is started over once it grows past this, so it can never fill the disk.</summary>
    internal const long MaxCrashLogBytes = 512 * 1024;

    internal const string NotificationText =
        "Something went wrong inside WinXCornersPlus. It is still running. Details are in the log window and in crash.log.";

    /// <summary>The notification when no crash.log is written (an elevated session).</summary>
    internal const string NotificationTextWithoutFile =
        "Something went wrong inside WinXCornersPlus. It is still running. Details are in the log window.";

    private readonly string? _crashLogPath;
    private readonly Lock _sync = new();
    private BoundedLog? _log;
    private Action<string>? _notify;
    private bool _notified;
    private bool _installed;

    /// <param name="crashLogPath">Full path of crash.log (tests use a temp folder), or null to write no file.</param>
    internal CrashReporter(string? crashLogPath)
    {
        if (crashLogPath is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(crashLogPath);
        }

        _crashLogPath = crashLogPath;
    }

    /// <summary>Where crash.log is written, or null when this reporter writes no file.</summary>
    internal string? CrashLogPath => _crashLogPath;

    /// <summary>Why the last write to crash.log failed, or null.</summary>
    internal string? LastCrashLogError { get; private set; }

    /// <summary>%LOCALAPPDATA%\WinXCornersPlus\crash.log (local, not roaming: it is about this machine).</summary>
    internal static string DefaultCrashLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinXCornersPlus",
            "crash.log");
    }

    /// <summary>Routes UI-thread exceptions to <see cref="ReportUiException"/>. Call before any window exists.</summary>
    internal void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    internal void Uninstall()
    {
        if (!_installed)
        {
            return;
        }

        _installed = false;
        Application.ThreadException -= OnThreadException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
    }

    /// <summary>Once the host exists, exceptions are also written to the in-app log.</summary>
    internal void AttachLog(BoundedLog log)
    {
        lock (_sync)
        {
            _log = log;
        }
    }

    /// <summary>Once the tray icon exists, the first UI exception is shown with this (a balloon).</summary>
    internal void AttachNotifier(Action<string>? notify)
    {
        lock (_sync)
        {
            _notify = notify;
        }
    }

    /// <summary>An exception escaped a UI event handler: log it, notify once, keep running.</summary>
    internal void ReportUiException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Record("UI thread", exception.ToString(), exception);

        Action<string>? notify;
        lock (_sync)
        {
            if (_notified)
            {
                return;
            }

            notify = _notify;
            _notified = notify is not null;
        }

        if (notify is null)
        {
            return;
        }

        try
        {
            notify(_crashLogPath is null ? NotificationTextWithoutFile : NotificationText);
        }
        catch (InvalidOperationException ex)
        {
            // Includes ObjectDisposedException: the tray icon went away during shutdown.
            Record("notification", ex.ToString(), ex);
        }
        catch (Win32Exception ex)
        {
            Record("notification", ex.ToString(), ex);
        }
    }

    /// <summary>An exception escaped a non-UI thread; the runtime ends the process when <paramref name="isTerminating"/>.</summary>
    internal void ReportFatal(object? exceptionObject, bool isTerminating)
    {
        var text = exceptionObject?.ToString() ?? "(no exception object)";
        Record(isTerminating ? "background thread, process ending" : "background thread", text, exceptionObject as Exception);
    }

    private void OnThreadException(object? sender, ThreadExceptionEventArgs e) => ReportUiException(e.Exception);

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) => ReportFatal(e.ExceptionObject, e.IsTerminating);

    private void Record(string where, string details, Exception? exception)
    {
        BoundedLog? log;
        lock (_sync)
        {
            log = _log;
        }

        var summary = exception is null ? "unknown error" : $"{exception.GetType().Name}: {exception.Message}";
        log?.Add($"Unexpected error ({where}): {summary}");
        AppendToCrashLog(where, details);
    }

    /// <summary>Best effort: a crash log that cannot be written must not cause a second failure.</summary>
    private void AppendToCrashLog(string where, string details)
    {
        if (_crashLogPath is null)
        {
            return;
        }

        var entry = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(where).Append("] ")
            .AppendLine(details)
            .AppendLine()
            .ToString();

        lock (_sync)
        {
            try
            {
                var folder = Path.GetDirectoryName(_crashLogPath);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                var info = new FileInfo(_crashLogPath);
                var append = !info.Exists || info.Length < MaxCrashLogBytes;
                using var writer = new StreamWriter(_crashLogPath, append, Encoding.UTF8);
                writer.Write(entry);
                LastCrashLogError = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
            {
                // Disk full, file locked, no access: remembered for tests; the in-app log still has the summary.
                LastCrashLogError = ex.Message;
            }
        }
    }
}
