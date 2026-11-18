using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>The About window: version, credits and links (opened without administrator rights through the host).</summary>
internal sealed class AboutForm : Form
{
    internal const string AsteskiUrl = "https://github.com/Asteski";
    internal const string VhanlaUrl = "https://github.com/vhanla";

    private readonly Font _baseFont;
    private readonly Font _titleFont;
    private readonly Icon? _icon;
    private readonly ToolTip _toolTip;
    private readonly UiScaling _scaling;

    internal AboutForm(IAppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        Name = "aboutForm";
        Text = "About WinXCornersPlus";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        // Layout in 96-DPI pixels, scaled by UiScaling. Height: the original 178 plus 44 for the two status
        // lines (8 gap + 16 + 4 gap + 16), keeping the original 22 below the last line.
        ClientSize = new Size(228, 222);
        BackColor = ThemeHelper.Colors.GetBackgroundColor();
        ForeColor = ThemeHelper.Colors.GetForegroundColor();
        _baseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Font = _baseFont;
        _titleFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
        _icon = WindowIconLoader.TryLoadAboutIcon();
        if (_icon is not null)
        {
            Icon = _icon;
        }

        HandleCreated += (_, _) => ThemeHelper.ApplyNativeWindowTheme(Handle);
        _toolTip = new ToolTip();

        var titleLabel = new Label
        {
            Name = "titleLabel",
            Text = "WinXCornersPlus",
            Font = _titleFont,
            AutoSize = false,
            Location = new Point(18, 16),
            Size = new Size(200, 28),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };

        // The version and the credits are separate labels, each sized to its text. (The original single label
        // held nine lines in a 72 px box: the copyright line and two duplicate URLs were always cut off.)
        var versionLabel = new Label
        {
            Name = "versionLabel",
            Text = $"Version: {DisplayText.ForDisplay(host.VersionText, 40)}",
            AutoSize = false,
            UseMnemonic = false,
            Location = new Point(20, 48),
            Size = new Size(190, 16),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };

        var creditsLabel = new Label
        {
            Name = "creditsLabel",
            Text = "Created by Adam Kamienski\r\nOriginally developed by vhanla",
            AutoSize = false,
            UseMnemonic = false,
            Location = new Point(20, 78),
            Size = new Size(190, 36),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };

        var githubAsteskiLink = CreateLink("asteskiLink", AsteskiUrl, 120);
        githubAsteskiLink.LinkClicked += (_, _) => host.OpenLink(AsteskiUrl);

        var githubVhanlaLink = CreateLink("vhanlaLink", VhanlaUrl, 140);
        githubVhanlaLink.LinkClicked += (_, _) => host.OpenLink(VhanlaUrl);

        // Links end at y 156; the status lines follow 8 px lower, 20 px apart like the links.
        var elevatedLabel = new Label
        {
            Name = "elevatedLabel",
            Text = $"Running as administrator: {(host.IsElevated ? "Yes" : "No")}",
            AutoSize = false,
            Location = new Point(20, 164),
            Size = new Size(190, 16),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };

        var settingsPath = host.GetSecurityContext().SettingsPath;
        var shownPath = string.IsNullOrWhiteSpace(settingsPath) ? "(unknown)" : DisplayText.ForDisplay(settingsPath, 260);
        var settingsPathLabel = new PathLabel
        {
            Name = "settingsPathLabel",
            Text = shownPath,
            AutoSize = false,
            Location = new Point(20, 184),
            Size = new Size(190, 16),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            AccessibleName = "Settings file " + shownPath
        };
        _toolTip.SetToolTip(settingsPathLabel, shownPath);

        Controls.Add(titleLabel);
        Controls.Add(versionLabel);
        Controls.Add(creditsLabel);
        Controls.Add(githubAsteskiLink);
        Controls.Add(githubVhanlaLink);
        Controls.Add(elevatedLabel);
        Controls.Add(settingsPathLabel);

        _scaling = new UiScaling(this);
        _scaling.Apply();
    }

    /// <summary>The hand-placed layout's scaler; UI tests use it to simulate higher DPI.</summary>
    internal UiScaling Scaling => _scaling;

    /// <summary>Holds the full settings path for the shortened path label.</summary>
    internal ToolTip PathToolTip => _toolTip;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The window may be created on a monitor whose DPI differs from the one measured in the constructor.
        _scaling?.Apply();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == UiScaling.WmDpiChanged)
        {
            _scaling?.Apply();
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _toolTip.Dispose();
            _titleFont.Dispose();
            _baseFont.Dispose();
            _icon?.Dispose();
        }
    }

    private static LinkLabel CreateLink(string name, string url, int y)
    {
        return new LinkLabel
        {
            Name = name,
            Text = url,
            AutoSize = false,
            Location = new Point(20, y),
            Size = new Size(190, 16),
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetAccentColor()
        };
    }

    /// <summary>A one-line label that shortens a long path in the middle ("C:\Users\…\settings.json").</summary>
    private sealed class PathLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            // Background is painted by OnPaintBackground; only the text is drawn differently from a Label.
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
