using System.Windows.Forms.VisualStyles;

namespace WinXCorners.App;

/// <summary>The themed flat button used in the Settings window footer (OK, Apply, Cancel).</summary>
internal sealed class SettingsFooterButton : Button
{
    private static readonly Color DarkDisabledForeground = Color.FromArgb(140, 140, 140);

    private bool _isHovered;
    private bool _isPressed;

    internal SettingsFooterButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        UseVisualStyleBackColor = true;
        FlatStyle = FlatStyle.Standard;
        TabStop = true;
    }

    /// <summary>
    /// Whether this is the button Enter would press: the window's AcceptButton unless another button has the focus.
    /// <see cref="ButtonBase.IsDefault"/> alone is only updated once the window has been activated, so a window
    /// shown in the background would otherwise not mark its default button.
    /// </summary>
    private bool ShowsAsDefault =>
        IsDefault ||
        (FindForm() is { } form && ReferenceEquals(form.AcceptButton, this) &&
         !(form.ActiveControl is Button other && !ReferenceEquals(other, this) && other.Focused));

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!ThemeHelper.IsLightTheme)
        {
            PaintDark(e.Graphics);
            return;
        }

        ButtonRenderer.DrawParentBackground(e.Graphics, ClientRectangle, this);

        // The window's default button (AcceptButton) gets the accent outline, as native buttons do.
        var state = Enabled
            ? _isPressed
                ? PushButtonState.Pressed
                : _isHovered
                    ? PushButtonState.Hot
                    : ShowsAsDefault
                        ? PushButtonState.Default
                        : PushButtonState.Normal
            : PushButtonState.Disabled;

        ButtonRenderer.DrawButton(e.Graphics, ClientRectangle, Text, Font, Focused && ShowFocusCues, state);
    }

    /// <summary>
    /// The visual-styles renderer only draws light buttons, which glare in a dark window, so dark mode paints a
    /// flat button from the theme palette: accent outline for the default or focused button, dimmed when disabled.
    /// </summary>
    private void PaintDark(Graphics graphics)
    {
        var bounds = ClientRectangle;
        graphics.Clear(Parent?.BackColor ?? ThemeHelper.Colors.GetBackgroundColor());

        var background = !Enabled
            ? ThemeHelper.Colors.GetSettingsDisabledButtonBackgroundColor()
            : _isPressed
                ? ThemeHelper.Colors.GetSettingsFooterButtonPressedBackgroundColor()
                : _isHovered
                    ? ThemeHelper.Colors.GetSettingsFooterButtonHoverBackgroundColor()
                    : ThemeHelper.Colors.GetSettingsFooterButtonBackgroundColor();
        var emphasized = Enabled && (ShowsAsDefault || Focused);
        var border = !Enabled
            ? ThemeHelper.Colors.GetSettingsDisabledButtonBorderColor()
            : emphasized
                ? ThemeHelper.Colors.GetAccentColor()
                : ThemeHelper.Colors.GetSettingsFooterButtonBorderColor();
        // The palette's disabled text colour (75, 75, 75) is meant for light backgrounds and is nearly invisible on
        // the dark disabled fill, so dark mode uses a mid grey that is readable but clearly inactive.
        var foreground = Enabled ? ThemeHelper.Colors.GetForegroundColor() : DarkDisabledForeground;

        using (var brush = new SolidBrush(background))
        {
            graphics.FillRectangle(brush, bounds);
        }

        using (var pen = new Pen(border, emphasized ? 2 : 1) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
        {
            graphics.DrawRectangle(pen, new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1));
        }

        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        if (!ShowKeyboardCues)
        {
            flags |= TextFormatFlags.HidePrefix;
        }

        TextRenderer.DrawText(graphics, Text, Font, bounds, foreground, flags);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(graphics, Rectangle.Inflate(bounds, -4, -4), foreground, background);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }
}