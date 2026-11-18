namespace WinXCorners.App;

/// <summary>
/// Keeps at most one instance of a non-modal window: the first request creates and shows it, later requests bring
/// the same window to the front. A window that hides instead of closing (the log window) is simply reused.
/// </summary>
internal sealed class SingleWindow<T> : IDisposable
    where T : Form
{
    private readonly Func<T> _create;
    private T? _window;

    internal SingleWindow(Func<T> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    /// <summary>The window, while it exists.</summary>
    internal T? Current => _window is { IsDisposed: false } window ? window : null;

    /// <summary>Shows the window (creating it first if needed) and brings it to the front.</summary>
    /// <param name="beforeFirstShow">Runs once on a new window before it is shown, e.g. to position it.</param>
    internal T Show(Action<T>? beforeFirstShow = null)
    {
        if (Current is not { } window)
        {
            window = _create();
            var created = window;
            // A non-modal form disposes itself after FormClosed; only the reference is dropped here.
            created.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_window, created))
                {
                    _window = null;
                }
            };
            _window = created;
            beforeFirstShow?.Invoke(created);
        }

        if (window.WindowState == FormWindowState.Minimized)
        {
            window.WindowState = FormWindowState.Normal;
        }

        window.Show();
        window.BringToFront();
        window.Activate();
        return window;
    }

    /// <summary>
    /// Destroys the window without asking it to close: a close request can be vetoed or turned into a hide (the log
    /// window does that for the user's own clicks), which must never block shutdown (review B1).
    /// </summary>
    public void Dispose()
    {
        _window?.Dispose();
        _window = null;
    }
}
