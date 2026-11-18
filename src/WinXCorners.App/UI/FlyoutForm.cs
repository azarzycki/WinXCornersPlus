using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using WinXCorners.Core.Actions;
using WinXCorners.Core.HotCorners;
using WinXCorners.Core.Layout;
using WinXCorners.Core.Model;
using WinXCorners.Core.Settings;

namespace WinXCorners.App;

/// <summary>
/// The tray flyout (the original Form1's look): four corner buttons around the accent "monitor" tile, sliding in
/// from behind the taskbar. Clicking a button opens the action picker; a pick is stored through the settings session.
/// </summary>
/// <remarks>
/// Positions and timing come from Core's <see cref="FlyoutLayout"/> and <see cref="FlyoutAnimation"/>; the animation
/// is driven by a ~10 ms WinForms timer and a <see cref="Stopwatch"/> (review B8: no BeginInvoke busy loop, no
/// timeBeginPeriod).
/// </remarks>
internal sealed class FlyoutForm : Form
{
    private const int AnimationIntervalMs = 10;
    private const long ReopenGuardMs = 250;
    private const int CornerRadius = 6;

    // The layout at 96 DPI, unchanged from the original.
    private const int BaseLeftX = 16;
    private const int BaseRightX = 258;
    private const int BaseTopY = 17;
    private const int BaseTileTop = 66;
    private const int CornerMenuItemHeight = 24;
    private const int CornerMenuPadding = 8;
    private const int CornerMenuWidth = 220;
    private static readonly Size BaseClientSize = new(424, 228);
    private static readonly Size BaseTileSize = new(171, 96);
    private static readonly Size BaseButtonSize = new(148, 28);

    private readonly SettingsSession _settings;
    private readonly Dictionary<HotCornerArea, FlyoutActionControl> _cornerButtons = [];
    private readonly AccentTileControl _monitorTile;
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly Stopwatch _animationClock = new();
    private readonly Icon? _icon;
    private bool _closing;
    private Point _animationFrom;
    private Point _animationTo;
    private long _lastDeactivateCloseMs = long.MinValue / 2;
    private double _scaleFactor = 1.0;

    internal FlyoutForm(SettingsSession settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;

        Name = "flyoutForm";
        Text = MainForm.TrayText;
        AutoScaleMode = AutoScaleMode.None; // Scaled by hand in ApplyScaling.
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = false; // Toggled with SetWindowPos: behind the taskbar while sliding, topmost once open.
        DoubleBuffered = true;
        Padding = Padding.Empty;
        BackColor = ThemeHelper.Colors.GetFlyoutBackgroundColor();
        _icon = WindowIconLoader.TryLoadAppIcon();
        Icon = _icon;
        Opacity = 0;

        AddCornerButton(HotCornerArea.TopLeft, "topLeftButton");
        AddCornerButton(HotCornerArea.TopRight, "topRightButton");
        AddCornerButton(HotCornerArea.BottomLeft, "bottomLeftButton");
        AddCornerButton(HotCornerArea.BottomRight, "bottomRightButton");
        _monitorTile = new AccentTileControl
        {
            Name = "monitorTile",
            CornerRadius = CornerRadius,
            AccentColor = ThemeHelper.Colors.GetSystemAccentColor(),
            Text = "1",
            ForeColor = Color.White
        };
        Controls.Add(_monitorTile);

        _animationTimer = new System.Windows.Forms.Timer { Interval = AnimationIntervalMs };
        _animationTimer.Tick += (_, _) => AdvanceAnimation();

        ShowCornerMenu = (point, items) => Win32PopupMenu.ShowCornerMenu(Handle, point, items);
        RefreshLabels();
        ApplyScaling(1.0);
    }

    /// <summary>Shows the corner picker at a screen point and returns the chosen action id (null = none). Test seam.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<Point, IReadOnlyList<CornerMenuItem>, string?> ShowCornerMenu { get; set; }

    /// <summary>Open, opening or closing (hot corners are suppressed meanwhile).</summary>
    internal bool IsOpen => Visible;

    internal bool IsCornerMenuOpen { get; private set; }

    internal bool IsAnimating => _animationTimer.Enabled;

    internal FlyoutActionControl GetCornerButton(HotCornerArea area) => _cornerButtons[area];

    /// <summary>The tray icon's left click: open or close, except right after a click elsewhere closed it.</summary>
    internal void ToggleFromTray()
    {
        if (!Visible && Environment.TickCount64 - _lastDeactivateCloseMs < ReopenGuardMs)
        {
            return; // The click on the tray icon is what just deactivated (and closed) the flyout.
        }

        if (Visible && !_closing)
        {
            Dismiss();
        }
        else
        {
            Open();
        }
    }

    /// <summary>Slides in from behind the taskbar and takes the focus.</summary>
    internal void Open()
    {
        if (!IsHandleCreated)
        {
            CreateHandle(); // First, so the DPI scaling has been applied before the size is used for placement.
        }

        var (anchor, offscreen) = GetPlacement();
        _closing = false;
        _animationFrom = offscreen;
        _animationTo = anchor;
        SetTopMost(false);
        Location = offscreen;
        Opacity = 0;
        Show();
        Activate();
        StartAnimation();
    }

    /// <summary>Slides out and hides. The flyout is only hidden, never closed, while the app runs.</summary>
    internal void Dismiss()
    {
        if (!Visible || _closing)
        {
            return;
        }

        _closing = true;
        _animationFrom = Location;
        _animationTo = GetPlacement().Offscreen;
        SetTopMost(false);
        StartAnimation();
    }

    /// <summary>Hides at once, without animation (shutdown).</summary>
    internal void HideNow()
    {
        _animationTimer.Stop();
        _closing = false;
        Opacity = 0;
        Hide();
    }

    /// <summary>Jumps a running animation to its end state.</summary>
    internal void CompleteAnimationForTest()
    {
        if (_animationTimer.Enabled)
        {
            FinishAnimation();
        }
    }

    /// <summary>Shows the action picker for a corner; a pick is stored through the settings session.</summary>
    internal void OpenCornerMenu(HotCornerArea area)
    {
        if (!_cornerButtons.TryGetValue(area, out var button))
        {
            return;
        }

        var items = ActionCatalog.BuildCornerMenu(_settings.Current);
        var menuHeight = DpiScale.Scale(CornerMenuPadding + (items.Count * CornerMenuItemHeight), _scaleFactor);
        var menuWidth = DpiScale.Scale(CornerMenuWidth, _scaleFactor);
        var origin = button.PointToScreen(Point.Empty);
        var workArea = Screen.FromControl(this).WorkingArea;
        var preferAbove = area is HotCornerArea.BottomLeft or HotCornerArea.BottomRight;
        var top = PopupPlacementCalculator.CalculateTop(origin.Y, menuHeight, workArea.Top, workArea.Bottom, preferAbove);
        var left = Math.Clamp(origin.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - menuWidth));

        string? picked;
        IsCornerMenuOpen = true;
        try
        {
            picked = ShowCornerMenu(new Point(left, top), items);
        }
        finally
        {
            IsCornerMenuOpen = false;
        }

        if (picked is not null)
        {
            _settings.Update(settings => SetCornerAction(settings, area, picked));
        }
    }

    /// <summary>Button labels from the live settings (Core's <see cref="ActionCatalog.GetButtonText"/>).</summary>
    internal void RefreshLabels()
    {
        var settings = _settings.Current;
        foreach (var (area, button) in _cornerButtons)
        {
            button.Text = ActionCatalog.GetButtonText(settings, HotCornerRules.GetActionId(settings, area));
        }
    }

    /// <summary>Re-reads colours after <see cref="ThemeHelper.Invalidate"/>.</summary>
    internal void RefreshTheme()
    {
        if (IsHandleCreated)
        {
            ThemeHelper.ApplyNativeWindowTheme(Handle);
        }

        BackColor = ThemeHelper.Colors.GetFlyoutBackgroundColor();
        _monitorTile.AccentColor = ThemeHelper.Colors.GetSystemAccentColor();
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ThemeHelper.ApplyNativeWindowTheme(Handle);
        ApplyScaling(UiScaling.GetScaleFactor(this));
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == UiScaling.WmDpiChanged)
        {
            ApplyScaling(UiScaling.GetScaleFactor(this));
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);

        // Clicking anywhere else closes the flyout, except while its own corner menu has the focus.
        if (!IsCornerMenuOpen && Visible && !_closing)
        {
            _lastDeactivateCloseMs = Environment.TickCount64;
            Dismiss();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Alt+F4 while the flyout has the focus would otherwise destroy it for the rest of the session.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Dismiss();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;

        var bounds = GetBackgroundBounds();
        using var path = RoundedRectangleGeometry.CreatePath(bounds, CornerRadius);
        using var brush = new SolidBrush(ThemeHelper.Colors.GetFlyoutBackgroundColor());
        using var pen = new Pen(ThemeHelper.Colors.GetFlyoutBorderColor(), 1f) { Alignment = PenAlignment.Inset };
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutControls();
        UpdateRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animationTimer.Dispose();
            _icon?.Dispose();
            var region = Region;
            Region = null;
            region?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AddCornerButton(HotCornerArea area, string name)
    {
        var button = new FlyoutActionControl { Name = name };
        button.Click += (_, _) => OpenCornerMenu(area);
        _cornerButtons[area] = button;
        Controls.Add(button);
    }

    private static void SetCornerAction(ApplicationSettings settings, HotCornerArea area, string actionId)
    {
        switch (area)
        {
            case HotCornerArea.TopLeft:
                settings.TopLeftActionId = actionId;
                break;
            case HotCornerArea.TopRight:
                settings.TopRightActionId = actionId;
                break;
            case HotCornerArea.BottomLeft:
                settings.BottomLeftActionId = actionId;
                break;
            case HotCornerArea.BottomRight:
                settings.BottomRightActionId = actionId;
                break;
        }
    }

    private (Point Anchor, Point Offscreen) GetPlacement()
    {
        var screen = Screen.PrimaryScreen ?? Screen.FromPoint(Cursor.Position);
        var workArea = screen.WorkingArea;
        var taskbar = TaskbarInfo.TryGetPrimaryTaskbar() ?? new TaskbarInfo(workArea, TaskbarEdge.Bottom);
        var anchor = FlyoutLayout.GetAnchor(workArea, Size, taskbar.Edge);
        var offscreen = FlyoutLayout.GetOffscreenLocation(
            anchor, Size, _settings.Current.FlyoutAnimationDirection, taskbar.Bounds, taskbar.Edge);
        return (anchor, offscreen);
    }

    private void StartAnimation()
    {
        _animationClock.Restart();
        _animationTimer.Start();
        AdvanceAnimation();
    }

    private void AdvanceAnimation()
    {
        var progress = _animationClock.Elapsed.TotalMilliseconds / FlyoutAnimation.Duration(_closing).TotalMilliseconds;
        if (progress >= 1)
        {
            FinishAnimation();
            return;
        }

        Location = FlyoutAnimation.Interpolate(_animationFrom, _animationTo, FlyoutAnimation.MoveProgress(progress, _closing));
        Opacity = FlyoutAnimation.Opacity(progress, _closing);
    }

    private void FinishAnimation()
    {
        _animationTimer.Stop();
        _animationClock.Reset();
        Location = _animationTo;
        if (_closing)
        {
            HideNow();
        }
        else
        {
            Opacity = 1;
            SetTopMost(true);
        }
    }

    private void SetTopMost(bool topMost)
    {
        if (IsHandleCreated)
        {
            _ = NativeMethods.SetWindowPos(
                Handle,
                topMost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
    }

    private void ApplyScaling(double factor)
    {
        _scaleFactor = factor;
        ClientSize = DpiScale.Scale(BaseClientSize, factor);
        _monitorTile.Size = DpiScale.Scale(BaseTileSize, factor);
        foreach (var button in _cornerButtons.Values)
        {
            button.Size = DpiScale.Scale(BaseButtonSize, factor);
        }

        LayoutControls();
        UpdateRegion();
    }

    private void LayoutControls()
    {
        if (_monitorTile is null)
        {
            return; // A resize during construction, before the controls exist.
        }

        var leftX = DpiScale.Scale(BaseLeftX, _scaleFactor);
        var rightX = DpiScale.Scale(BaseRightX, _scaleFactor);
        var topY = DpiScale.Scale(BaseTopY, _scaleFactor);

        var topLeft = _cornerButtons[HotCornerArea.TopLeft];
        topLeft.Location = new Point(leftX, topY);
        _cornerButtons[HotCornerArea.TopRight].Location = new Point(rightX, topY);
        _monitorTile.Location = new Point((ClientSize.Width - _monitorTile.Width) / 2, DpiScale.Scale(BaseTileTop, _scaleFactor));

        // The gap above the tile is repeated below it.
        var bottomY = _monitorTile.Bottom + (_monitorTile.Top - topLeft.Bottom);
        _cornerButtons[HotCornerArea.BottomLeft].Location = new Point(leftX, bottomY);
        _cornerButtons[HotCornerArea.BottomRight].Location = new Point(rightX, bottomY);
    }

    private RectangleF GetBackgroundBounds() => new(1.5f, 1.5f, Width - 3f, Height - 3f);

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = RoundedRectangleGeometry.CreatePath(GetBackgroundBounds(), CornerRadius);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }
}
