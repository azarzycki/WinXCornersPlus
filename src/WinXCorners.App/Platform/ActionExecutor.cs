using System.Collections.Concurrent;
using WinXCorners.Core.Actions;
using WinXCorners.Core.Diagnostics;
using WinXCorners.Core.Input;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// Runs corner actions on one dedicated background thread (review B4), so nothing slow - paced key sequences,
/// process launches, cross-process COM - ever runs on the UI thread or the input path. Core's
/// <see cref="ActionPlanner"/> decides what an action means; this class performs it.
/// </summary>
/// <remarks>
/// The queue is bounded: when <see cref="Capacity"/> requests are already waiting, a new one is dropped and logged
/// rather than letting a burst of corner hits replay long after the user stopped.
/// </remarks>
internal sealed class ActionExecutor : IDisposable
{
    internal const int Capacity = 8;
    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly BoundedLog _log;
    private readonly SystemDirectories _directories;
    private readonly IKeySender _keys;
    private readonly IProcessLauncher _launcher;
    private readonly IWindowQuery _windowQuery;
    private readonly Func<bool> _isTaskViewForeground;
    private readonly ISystemActions _systemActions;
    private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>(), Capacity);
    private readonly Thread _worker;
    private readonly Lock _sync = new();
    private int _pending;
    private TaskCompletionSource? _idle;
    private bool _disposed;
    private IntPtr _mainWindowHandle;

    /// <param name="log">Where every outcome is written.</param>
    /// <param name="directories">System folders for the absolute paths of built-in programs.</param>
    /// <param name="keys">Key injection (test seam).</param>
    /// <param name="launcher">Program/document/link launches (test seam).</param>
    /// <param name="windowQuery">Window lookups for conditional hotkeys (test seam).</param>
    /// <param name="isTaskViewForeground">Whether Task View is in front, asked when planning (test seam).</param>
    /// <param name="systemActions">WM_SYSCOMMAND and LockWorkStation (test seam).</param>
    internal ActionExecutor(
        BoundedLog log,
        SystemDirectories directories,
        IKeySender keys,
        IProcessLauncher launcher,
        IWindowQuery windowQuery,
        Func<bool> isTaskViewForeground,
        ISystemActions systemActions)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _windowQuery = windowQuery ?? throw new ArgumentNullException(nameof(windowQuery));
        _isTaskViewForeground = isTaskViewForeground ?? throw new ArgumentNullException(nameof(isTaskViewForeground));
        _systemActions = systemActions ?? throw new ArgumentNullException(nameof(systemActions));

        // STA because the de-elevated launch talks to the shell through COM, as the shell's own clients do.
        _worker = new Thread(Run) { IsBackground = true, Name = "WinXCorners action worker" };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    /// <summary>The app's main window; system commands (screen saver, display off, Start) are posted to it.</summary>
    public IntPtr MainWindowHandle
    {
        get => Volatile.Read(ref _mainWindowHandle);
        set => Volatile.Write(ref _mainWindowHandle, value);
    }

    /// <summary>
    /// Queues <paramref name="actionId"/>. Returns at once. <paramref name="settingsSnapshot"/> must be a copy
    /// (<c>SettingsSession.Snapshot()</c>): the worker reads it on another thread.
    /// </summary>
    public void Execute(string actionId, ApplicationSettings settingsSnapshot)
    {
        ArgumentNullException.ThrowIfNull(settingsSnapshot);
        var shownId = DisplayText.ForDisplay(actionId);
        lock (_sync)
        {
            if (_disposed)
            {
                _log.Add($"Action '{shownId}' ignored: the app is shutting down.");
                return;
            }

            if (!_queue.TryAdd(new WorkItem(actionId, settingsSnapshot)))
            {
                _log.Add($"Action '{shownId}' dropped: {Capacity} actions are already waiting.");
                return;
            }

            _pending++;
        }
    }

    /// <summary>Test seam: completes when every queued action has finished (at once when nothing is queued).</summary>
    internal Task WhenIdle()
    {
        lock (_sync)
        {
            if (_pending == 0)
            {
                return Task.CompletedTask;
            }

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return _idle.Task;
        }
    }

    /// <summary>Stops accepting work, skips what is still queued and waits up to 2 s for the running action.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
        }

        if (_worker.Join(DisposeWait))
        {
            _queue.Dispose();
        }
        else
        {
            // Leave the collection undisposed: the worker is still inside an action and will read it once more.
            _log.Add("The action worker did not finish within 2 seconds; it is abandoned (background thread).");
        }
    }

    private void Run()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            try
            {
                bool skip;
                lock (_sync)
                {
                    skip = _disposed;
                }

                if (skip)
                {
                    _log.Add($"Action '{DisplayText.ForDisplay(item.ActionId)}' skipped: the app is shutting down.");
                }
                else
                {
                    Perform(item.ActionId, item.Settings);
                }
            }
            catch (Exception ex)
            {
                // Last-resort guard for the worker thread only: an unexpected failure in one action (a bug, a
                // misbehaving shell extension) is logged and must not end the thread, or every later corner hit
                // would silently do nothing.
                _log.Add($"Action '{DisplayText.ForDisplay(item.ActionId)}' failed unexpectedly: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                TaskCompletionSource? idle = null;
                lock (_sync)
                {
                    _pending--;
                    if (_pending == 0)
                    {
                        idle = _idle;
                        _idle = null;
                    }
                }

                idle?.TrySetResult();
            }
        }
    }

    private void Perform(string actionId, ApplicationSettings settings)
    {
        var name = DisplayText.ForDisplay(ActionCatalog.GetDescriptiveName(settings, actionId));
        var plan = ActionPlanner.Plan(actionId, settings, _directories, _isTaskViewForeground());
        switch (plan)
        {
            case ActionPlan.Nothing nothing:
                _log.Add($"{name}: nothing to do. {nothing.Reason}");
                break;
            case ActionPlan.SendKeys sendKeys:
                SendSequence(name, sendKeys.Sequence);
                break;
            case ActionPlan.ConditionalKeys conditional:
                var selected = conditional.Expression.Select(_windowQuery);
                if (selected is null)
                {
                    _log.Add($"{name}: no window condition matched and there is no else-branch; nothing sent.");
                }
                else
                {
                    SendSequence(name, selected);
                }

                break;
            case ActionPlan.Launch launch:
                // ProcessLauncher logs the planned command line and its outcome itself.
                var launched = _launcher.TryLaunch(launch.Request, out var launchError);
                _log.Add(launched ? $"{name}: started." : $"{name}: not started. {launchError}");
                break;
            case ActionPlan.SystemCommand systemCommand:
                LogResult(name, _systemActions.TryPostSystemCommand(MainWindowHandle, systemCommand.Kind, out var commandError), commandError);
                break;
            case ActionPlan.LockWorkstation:
                LogResult(name, _systemActions.TryLockWorkstation(out var lockError), lockError);
                break;
            default:
                _log.Add($"{name}: unsupported plan {plan.GetType().Name}; nothing done.");
                break;
        }
    }

    private void SendSequence(string name, HotkeySequence sequence)
    {
        var events = KeyEventPlanner.Plan(sequence, _keys.GetModifiersDown());
        _keys.Send(events);
        _log.Add($"{name}: sent {events.Count} key events.");
    }

    private void LogResult(string name, bool succeeded, string? error)
    {
        _log.Add(succeeded ? $"{name}: done." : $"{name}: failed. {error}");
    }

    private sealed record WorkItem(string ActionId, ApplicationSettings Settings);
}
