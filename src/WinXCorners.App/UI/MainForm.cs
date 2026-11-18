using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinXCorners.Core.Model;
using WinXCorners.Core.Platform;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// The app's hidden main window (was Form1, review M1): owns the tray icon and the flyout, feeds input facts to the
/// hot-corner monitor, and forwards every decision to the platform layer (<see cref="AppHost"/>). It owns no
/// settings, persistence, state machine, launching or key sending. It is never shown.
/// </summary>
internal sealed class MainForm : Form
{
    internal const string TrayText = "WinXCornersPlus";
    internal const string TrayTextElevated = "WinXCornersPlus (Administrator)";
    internal const string ElevateHeading = "Before WinXCornersPlus runs as administrator";
    internal const string ContinueToWindowsPromptText = "Continue to the Windows prompt";

    private const int HotCornerIntervalMs = 50;
    private const int SaveDebounceMs = 180;
    private const int BalloonTimeoutMs = 5000;

    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmInput = 0x00FF;
    private const int WmThemeChanged = 0x031A;
    private const int WmDwmColorizationColorChanged = 0x0320;

    private readonly AppHost _host;
    private readonly Container _components = new();
    private readonly NotifyIcon _trayIcon;
    private readonly FlyoutForm _flyout;
    private readonly CountdownOverlay _countdown;
    private readonly System.Windows.Forms.Timer _hotCornerTimer;
    private readonly System.Windows.Forms.Timer _saveTimer;
    private readonly HotCornerMonitor _monitor;
    private readonly SingleWindow<AdvancedForm> _settingsWindow;
    private readonly SingleWindow<LogWindowForm> _logWindow;
    private readonly SingleWindow<AboutForm> _aboutWindow;
    private readonly SingleWindow<SecurityInfoForm> _securityWindow;

    private Rectangle[] _screenBounds = [];
    private Icon? _trayIconImage;
    private (bool Light, bool Enabled)? _trayIconKey;
    private bool _started;
    private bool _shuttingDown;
    private bool _rawInputRegistered;
    private bool _trayHiddenThisSession;
    private bool _trayForcedVisible;
    private bool _saveFailureLogged;

    /// <summary>Builds everything without showing anything (no tray icon, no window); <see cref="Start"/> does that.</summary>
    internal MainForm(AppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;

        Name = "mainForm";
        Text = TrayText;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;

        _flyout = new FlyoutForm(host.Settings);
        _countdown = new CountdownOverlay();
        _trayIcon = new NotifyIcon(_components) { Text = host.IsElevated ? TrayTextElevated : TrayText, Visible = false };
        _trayIcon.MouseUp += OnTrayMouseUp;

        _hotCornerTimer = new System.Windows.Forms.Timer(_components) { Interval = HotCornerIntervalMs };
        _hotCornerTimer.Tick += (_, _) => EvaluateHotCorners();
        _saveTimer = new System.Windows.Forms.Timer(_components) { Interval = SaveDebounceMs };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            FlushSettings();
        };

        // Each window is a single non-modal instance; edits go through the settings session, so none is modal.
        _settingsWindow = new SingleWindow<AdvancedForm>(() => new AdvancedForm(_host));
        _logWindow = new SingleWindow<LogWindowForm>(() => new LogWindowForm(_host.Log));
        _aboutWindow = new SingleWindow<AboutForm>(() => new AboutForm(_host));
        _securityWindow = new SingleWindow<SecurityInfoForm>(() => new SecurityInfoForm(_host));

        RefreshScreenBounds();
        _monitor = new HotCornerMonitor(
            () => _host.Settings.Current,
            () => Cursor.Position,
            ScreenBoundsAt,
            () => (MouseButtons & (MouseButtons.Left | MouseButtons.Middle | MouseButtons.Right)) != MouseButtons.None,
            _host.Keys.IsKeyDown,
            () => Environment.TickCount64,
            () => _flyout.IsOpen || _flyout.IsCornerMenuOpen,
            _host.Foreground.IsFullScreenForeground,
            _host.Log);
        _monitor.CountdownRequested += _countdown.ShowCountdown;
        _monitor.CountdownHidden += _countdown.HideCountdown;
        // The executor runs the action on its own thread, with a snapshot taken here on the UI thread.
        _monitor.ActionTriggered += actionId => _host.Executor.Execute(actionId, _host.Settings.Snapshot());

        // Seams with their production behaviour; Program and tests replace them.
        ExitMessageLoop = Application.ExitThread;
        ConfirmElevation = items => RiskDialog.Confirm(null, TrayText, ElevateHeading, items, ContinueToWindowsPromptText);
        ShowBalloon = (title, text, icon) =>
        {
            if (_trayIcon.Visible)
            {
                _trayIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);
            }
        };
        ShowTrayMenu = point => Win32PopupMenu.ShowTrayMenu(Handle, point, _host.Settings.Current.HotCornersEnabled, _host.IsElevated);

        _host.Settings.Changed += OnSettingsChanged;
        _host.ExitRequested += Shutdown;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        ApplyTrayIconImage();

        // Never shown, but the handle is needed now: system commands are posted to it, raw input is delivered to
        // it and other threads marshal to it. (Replaces the original Show(); Hide() trick.)
        CreateHandle();
    }

    // ---- Seams (replaced by Program and by tests) -------------------------------------------------

    /// <summary>Ends the message loop. Program sets it to its ApplicationContext's ExitThread.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action ExitMessageLoop { get; set; }

    /// <summary>Confirms "Restart as administrator" given the risks; defaults to <see cref="RiskDialog.Confirm"/>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<IReadOnlyList<RiskItem>, bool> ConfirmElevation { get; set; }

    /// <summary>Shows a tray balloon: (title, text, icon). Does nothing while the tray icon is hidden.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<string, string, ToolTipIcon> ShowBalloon { get; set; }

    /// <summary>Shows the tray menu at a screen point; returns the chosen command id (0 = none).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Point, int> ShowTrayMenu { get; set; }

    // ---- Test hooks -------------------------------------------------------------------------------

    internal FlyoutForm Flyout => _flyout;

    internal bool IsFlyoutOpen => _flyout.IsOpen;

    internal bool IsStarted => _started;

    internal bool IsShutDown => _shuttingDown;

    internal bool IsRawInputRegistered => _rawInputRegistered;

    internal NotifyIcon TrayIcon => _trayIcon;

    internal HotCornerMonitor HotCorners => _monitor;

    internal CountdownOverlay Countdown => _countdown;

    internal AdvancedForm? SettingsWindow => _settingsWindow.Current;

    internal LogWindowForm? LogWindow => _logWindow.Current;

    internal AboutForm? AboutWindow => _aboutWindow.Current;

    internal SecurityInfoForm? SecurityWindow => _securityWindow.Current;

    internal void HandleTrayCommandForTest(int commandId) => HandleTrayCommand(commandId);

    // ---- Lifetime ---------------------------------------------------------------------------------

    /// <summary>Shows the tray icon, starts hot corners and reports startup problems. Called once by Program.</summary>
    internal void Start()
    {
        if (_started || _shuttingDown)
        {
            return;
        }

        _started = true;
        UpdateTrayVisibility();
        _hotCornerTimer.Start();

        var notices = StartupNotices.Build(_host.LoadResult, () => _host.GetSecurityContext().SettingsPath, _host.Startup.GetStaleEntryPath());
        foreach (var notice in notices)
        {
            _host.Log.Add(notice);
        }

        if (notices.Count > 0)
        {
            ShowBalloon(TrayText, StartupNotices.ForBalloon(notices), ToolTipIcon.Info);
        }
    }

    /// <summary>
    /// The one shutdown path (reviews B1/B6/B7), run on <see cref="AppHost.ExitRequested"/>: stop, save, remove the
    /// tray icon, destroy every window, end the message loop. Nothing can veto it: windows are disposed, not asked
    /// to close, and the loop ends through ExitThread rather than Application.Exit.
    /// </summary>
    internal void Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        _hotCornerTimer.Stop();
        _saveTimer.Stop();
        _monitor.Reset();
        FlushSettings();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        DisposeWindows();
        _host.Log.Add("Exiting.");
        ExitMessageLoop();
    }

    /// <summary>Handler for <see cref="SingleInstance.ActivationRequested"/>, raised on a thread-pool thread.</summary>
    internal void OnActivationRequested() => RunOnUiThread(ShowSettingsFromExternalRequest);

    /// <summary>
    /// A second launch asked for Settings (brief design point 4). A hidden tray icon is shown again for the rest of
    /// this session, so a hidden icon can never lock the user out.
    /// </summary>
    internal void ShowSettingsFromExternalRequest()
    {
        if (_shuttingDown)
        {
            return;
        }

        _host.Log.Add("Another launch asked to open Settings.");
        if (_started && !_trayIcon.Visible)
        {
            _trayForcedVisible = true;
            UpdateTrayVisibility();
        }

        ShowSettingsWindow();
    }

    /// <summary>The crash reporter's one notification about an unexpected error.</summary>
    internal void ShowErrorNotification(string text) => ShowBalloon(TrayText, text, ToolTipIcon.Warning);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _host.Executor.MainWindowHandle = Handle;
        RegisterRawInput();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        // Raw input registrations belong to the window; a recreated handle registers again.
        _rawInputRegistered = false;
        if (_host.Executor.MainWindowHandle == Handle)
        {
            _host.Executor.MainWindowHandle = IntPtr.Zero;
        }

        base.OnHandleDestroyed(e);
    }

    protected override void SetVisibleCore(bool value)
    {
        // This window is never shown, whoever asks.
        base.SetVisibleCore(false);
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmInput:
                // Only a wake-up: the position is read from the cursor, not from the raw packet.
                EvaluateHotCorners();
                break;
            case WmDisplayChange:
                RefreshScreenBounds();
                _monitor.Reset();
                break;
            case WmSettingChange:
            case WmThemeChanged:
            case WmDwmColorizationColorChanged:
                RefreshTheme();
                break;
            case WmQueryEndSession:
            case WmEndSession:
                // Log-off, shutdown, or an installer / the Restart Manager closing the app for an upgrade or uninstall
                // (docs/INSTALLING.md). Exiting here is the clean path: settings saved, tray icon removed.
                switch (SessionEndPolicy.Decide(m.Msg, (long)m.WParam))
                {
                    case SessionEndResponse.SaveSettings:
                        FlushSettings();
                        break;
                    case SessionEndResponse.Exit:
                        Shutdown();
                        break;
                }

                break;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // SystemEvents is static: without this the form (and the host it references) would never be collected.
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _host.Settings.Changed -= OnSettingsChanged;
            _host.ExitRequested -= Shutdown;

            _hotCornerTimer.Stop();
            _saveTimer.Stop();
            _trayIcon.Visible = false;
            _components.Dispose();
            DisposeWindows();
            _flyout.Dispose();
            _countdown.Dispose();
            _trayIconImage?.Dispose();
            _trayIconImage = null;
        }

        base.Dispose(disposing);
    }

    // ---- Hot corners (review B4: raw input as a wake-up plus a 50 ms timer; no low-level hook) ----

    private void EvaluateHotCorners()
    {
        if (_started && !_shuttingDown)
        {
            _monitor.Evaluate();
        }
    }

    private void RegisterRawInput()
    {
        if (_rawInputRegistered)
        {
            return;
        }

        // RIDEV_INPUTSINK: WM_INPUT arrives although this window is never in the foreground.
        var device = new NativeMethods.RAWINPUTDEVICE
        {
            usUsagePage = NativeMethods.HID_USAGE_PAGE_GENERIC,
            usUsage = NativeMethods.HID_USAGE_GENERIC_MOUSE,
            dwFlags = NativeMethods.RIDEV_INPUTSINK,
            hwndTarget = Handle
        };

        _rawInputRegistered = NativeMethods.RegisterRawInputDevices([device], 1, (uint)Marshal.SizeOf<NativeMethods.RAWINPUTDEVICE>());
        if (!_rawInputRegistered)
        {
            _host.Log.Add($"Raw mouse input could not be registered (error {Marshal.GetLastPInvokeError()}); hot corners rely on the 50 ms timer.");
        }
    }

    private void RefreshScreenBounds()
    {
        _screenBounds = Array.ConvertAll(Screen.AllScreens, screen => screen.Bounds);
    }

    private Rectangle ScreenBoundsAt(Point point)
    {
        foreach (var bounds in _screenBounds)
        {
            if (bounds.Contains(point))
            {
                return bounds;
            }
        }

        // The cache is stale (a display change is still on its way): ask Windows directly.
        return Screen.FromPoint(point).Bounds;
    }

    // ---- Settings ---------------------------------------------------------------------------------

    private void OnSettingsChanged(ApplicationSettings settings)
    {
        _flyout.RefreshLabels();
        ApplyTrayIconImage();
        UpdateTrayVisibility();
        _monitor.Reset();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSettings()
    {
        var result = _host.Settings.Flush();
        if (result.Succeeded)
        {
            _saveFailureLogged = false;
        }
        else if (!_saveFailureLogged)
        {
            // Logged once per failure streak: the session stays dirty and every later flush retries.
            _saveFailureLogged = true;
            _host.Log.Add($"Settings could not be saved: {result.Error}");
        }
    }

    // ---- Tray -------------------------------------------------------------------------------------

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            HandleTrayCommand(ShowTrayMenu(Cursor.Position));
        }
        else if (e.Button == MouseButtons.Left && !_shuttingDown)
        {
            _flyout.ToggleFromTray();
        }
    }

    private void HandleTrayCommand(int commandId)
    {
        switch (commandId)
        {
            case Win32PopupMenu.TrayCommandToggleHotCorners:
                // Through the session, so it is saved like every other change (review B6).
                _host.Settings.Update(settings => settings.HotCornersEnabled = !settings.HotCornersEnabled);
                _host.Log.Add(_host.Settings.Current.HotCornersEnabled ? "Hot corners enabled." : "Hot corners disabled.");
                break;
            case Win32PopupMenu.TrayCommandHideTray:
                _trayHiddenThisSession = true;
                _trayForcedVisible = false;
                UpdateTrayVisibility();
                _host.Log.Add("Tray icon hidden until WinXCornersPlus restarts. Start WinXCornersPlus again to open Settings and show it.");
                break;
            case Win32PopupMenu.TrayCommandElevate:
                RestartAsAdministrator();
                break;
            case Win32PopupMenu.TrayCommandReload:
                Restart(RestartMode.SameRights);
                break;
            case Win32PopupMenu.TrayCommandLogWindow:
                ShowWindow(_logWindow);
                break;
            case Win32PopupMenu.TrayCommandAdvanced:
                ShowSettingsWindow();
                break;
            case Win32PopupMenu.TrayCommandSecurity:
                ShowWindow(_securityWindow);
                break;
            case Win32PopupMenu.TrayCommandAbout:
                ShowWindow(_aboutWindow);
                break;
            case Win32PopupMenu.TrayCommandExit:
                _host.RequestExit();
                break;
        }
    }

    private void RestartAsAdministrator()
    {
        if (_host.IsElevated)
        {
            return;
        }

        var risks = SecurityPosture.DescribeElevation(_host.Settings.Current, _host.GetSecurityContext().Install, everyStart: false);
        if (!ConfirmElevation(risks))
        {
            _host.Log.Add("Restart as administrator cancelled.");
            return;
        }

        Restart(RestartMode.AsAdministrator);
    }

    private void Restart(RestartMode mode)
    {
        // On success the host raises ExitRequested, which runs Shutdown. On failure the host has logged why; the
        // user is told too, and the app keeps running.
        if (!_host.TryRestart(mode, out var error))
        {
            ShowBalloon(TrayText, $"WinXCornersPlus could not restart: {error ?? "unknown reason"}. It keeps running.", ToolTipIcon.Warning);
        }
    }

    private void UpdateTrayVisibility()
    {
        _trayIcon.Visible = _started && !_shuttingDown &&
            (_trayForcedVisible || (!_host.Settings.Current.AlwaysHideTrayIcon && !_trayHiddenThisSession));
    }

    private void ApplyTrayIconImage()
    {
        var key = (ThemeHelper.IsLightTheme, _host.Settings.Current.HotCornersEnabled);
        if (_trayIconKey == key)
        {
            return;
        }

        _trayIconKey = key;
        var icon = WindowIconLoader.TryLoadTrayIcon(key.Item1, key.Item2) ?? WindowIconLoader.LoadFallbackTrayIcon();
        var previous = _trayIconImage;
        _trayIconImage = icon;
        _trayIcon.Icon = icon;
        previous?.Dispose();
    }

    // ---- Windows ----------------------------------------------------------------------------------

    /// <summary>Opens Settings near the tray, or brings the open one to the front.</summary>
    internal void ShowSettingsWindow()
    {
        if (!_shuttingDown)
        {
            _flyout.Dismiss();
            _settingsWindow.Show(window => window.PositionNearTaskbarTray());
        }
    }

    private void ShowWindow<T>(SingleWindow<T> window)
        where T : Form
    {
        if (!_shuttingDown)
        {
            window.Show();
        }
    }

    private void DisposeWindows()
    {
        _settingsWindow.Dispose();
        _logWindow.Dispose();
        _aboutWindow.Dispose();
        _securityWindow.Dispose();
        _flyout.HideNow();
        _countdown.HideCountdown();
    }

    // ---- Theme and system events ------------------------------------------------------------------

    private void RefreshTheme()
    {
        ThemeHelper.Invalidate();
        ThemeHelper.ApplyNativeMenuTheme();
        _flyout.RefreshTheme();
        _countdown.RefreshTheme();
        ApplyTrayIconImage();
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) => RunOnUiThread(RefreshTheme);

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            RefreshScreenBounds();
            _monitor.Reset();
        });
    }

    /// <summary>Runs <paramref name="action"/> on the UI thread; dropped once the window is gone.</summary>
    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        if (!InvokeRequired)
        {
            action();
            return;
        }

        try
        {
            BeginInvoke(action);
        }
        catch (InvalidOperationException ex)
        {
            // The handle was destroyed between the check and the call (shutdown in progress).
            _host.Log.Add($"A notification from another thread was dropped during shutdown: {ex.Message}");
        }
    }
}
