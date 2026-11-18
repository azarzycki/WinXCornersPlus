using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using WinXCorners.Core.Model;

namespace WinXCorners.App;

/// <summary>
/// The small round badge that counts down in a hot corner while a delayed action is pending. It never takes
/// focus or activation.
/// </summary>
/// <remarks>
/// <see cref="ShowCountdown"/> runs on every 50 ms hot-corner tick, so it allocates nothing and repaints only
/// when the shown text changes (review B8: the original created a Font per tick). The font is the shared
/// instance from <see cref="ThemeHelper"/> and is not disposed here.
/// </remarks>
internal sealed class CountdownOverlay : Form
{
    private const int ScreenMargin = 8;
    private const int BadgeSize = 40;
    private string _text = "1";

    internal CountdownOverlay()
    {
        Name = "countdownOverlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(BadgeSize, BadgeSize);
        DoubleBuffered = true;
        ApplyTheme();
    }

    /// <summary>The text currently shown (for tests).</summary>
    internal string CountdownText => _text;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            const int wsExNoActivate = 0x08000000;

            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExToolWindow | wsExNoActivate;
            return createParams;
        }
    }

    /// <summary>Shows <paramref name="remaining"/> in <paramref name="area"/> of the screen with <paramref name="screenBounds"/>.</summary>
    internal void ShowCountdown(Rectangle screenBounds, HotCornerArea area, TimeSpan remaining)
    {
        var text = FormatRemaining(remaining);
        var textChanged = !string.Equals(text, _text, StringComparison.Ordinal);
        _text = text;

        var location = GetLocation(screenBounds, area, Size);
        if (Location != location)
        {
            Location = location;
        }

        if (!Visible)
        {
            Show();
        }

        if (textChanged)
        {
            Invalidate();
        }
    }

    internal void HideCountdown()
    {
        if (Visible)
        {
            Hide();
        }
    }

    /// <summary>Re-reads the theme colours (after <see cref="ThemeHelper.Invalidate"/>).</summary>
    internal void RefreshTheme()
    {
        ApplyTheme();
        if (Visible)
        {
            Invalidate();
        }
    }

    /// <summary>Whole seconds rounded up from one second on, tenths below it.</summary>
    internal static string FormatRemaining(TimeSpan remaining)
    {
        var seconds = Math.Max(0, remaining.TotalSeconds);
        return seconds >= 1
            ? Math.Ceiling(seconds).ToString("0", CultureInfo.CurrentCulture)
            : seconds.ToString("0.0", CultureInfo.CurrentCulture);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        var previous = Region;
        using var path = new GraphicsPath();
        path.AddEllipse(ClientRectangle);
        Region = new Region(path);
        previous?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        var foreground = ThemeHelper.Colors.GetCountdownForegroundColor();
        using var fillBrush = new SolidBrush(ThemeHelper.Colors.GetCountdownBackgroundColor());
        using var borderPen = new Pen(Color.FromArgb(48, foreground));

        e.Graphics.FillEllipse(fillBrush, bounds);
        e.Graphics.DrawEllipse(borderPen, bounds);
        TextRenderer.DrawText(
            e.Graphics,
            _text,
            Font,
            bounds,
            foreground,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The region is ours; the font is ThemeHelper's shared instance and must not be disposed.
            var region = Region;
            Region = null;
            region?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyTheme()
    {
        BackColor = ThemeHelper.Colors.GetCountdownBackgroundColor();
        Font = ThemeHelper.Colors.GetCountdownFont(compact: false);
    }

    private static Point GetLocation(Rectangle bounds, HotCornerArea area, Size overlaySize)
    {
        return area switch
        {
            HotCornerArea.TopRight => new Point(bounds.Right - overlaySize.Width - ScreenMargin, bounds.Top + ScreenMargin),
            HotCornerArea.BottomLeft => new Point(bounds.Left + ScreenMargin, bounds.Bottom - overlaySize.Height - ScreenMargin),
            HotCornerArea.BottomRight => new Point(bounds.Right - overlaySize.Width - ScreenMargin, bounds.Bottom - overlaySize.Height - ScreenMargin),
            _ => new Point(bounds.Left + ScreenMargin, bounds.Top + ScreenMargin)
        };
    }
}
