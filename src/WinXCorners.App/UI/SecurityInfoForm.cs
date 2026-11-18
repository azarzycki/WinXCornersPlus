using WinXCorners.Core.Model;
using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// The "Security &amp; privacy" page (brief design point 5): what the app does, the facts it is running with,
/// and every risk that is currently true, all in Core's words. The content is rebuilt from a fresh
/// <see cref="IAppHost.GetSecurityContext"/> whenever the window is activated or the settings change, so it
/// never shows a stale posture.
/// </summary>
internal sealed class SecurityInfoForm : Form
{
    internal const string NothingToReportText = "Nothing here needs your attention.";

    private const int ContentPadding = 16;

    private readonly IAppHost _host;
    private readonly Font _baseFont;
    private readonly Font _sectionFont;
    private readonly Font _boldFont;
    private readonly Icon? _icon;
    private readonly Panel _scrollPanel;
    private readonly TableLayoutPanel _content;
    private readonly List<IDisposable> _contentResources = [];

    // Controls whose width follows the window, with the room taken by whatever sits to their left.
    private readonly List<(Control Control, int Indent)> _widthTracked = [];
    private Func<int, int> _px;

    internal SecurityInfoForm(IAppHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        SuspendLayout();

        Name = "securityInfoForm";
        Text = "Security & privacy - WinXCornersPlus";
        AutoScaleMode = AutoScaleMode.None; // lengths are converted with _px
        _baseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Font = _baseFont;
        _sectionFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
        _boldFont = new Font(_baseFont, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        _icon = WindowIconLoader.TryLoadAppIcon();
        if (_icon is not null)
        {
            Icon = _icon;
        }

        DialogLayout.ApplyTheme(this);
        _px = DialogLayout.Scaler(this);
        ClientSize = new Size(_px(580), _px(560));
        MinimumSize = new Size(_px(420), _px(320));

        _content = DialogLayout.CreateColumn("securityContent");
        _content.Location = new Point(_px(ContentPadding), _px(ContentPadding));
        _content.Padding = new Padding(0, 0, 0, _px(ContentPadding));

        _scrollPanel = new Panel
        {
            Name = "securityScroller",
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = Padding.Empty,
            BackColor = Color.Transparent
        };
        _scrollPanel.Controls.Add(_content);

        var openFolderButton = DialogLayout.CreateButton("Open settings folder", "openSettingsFolderButton", _px);
        openFolderButton.Click += (_, _) => _host.OpenSettingsFolder();
        var closeButton = DialogLayout.CreateButton("Close", "closeButton", _px);
        closeButton.Click += (_, _) => Close();
        var buttonRow = DialogLayout.CreateButtonRow(openFolderButton, closeButton);
        buttonRow.Dock = DockStyle.Right;

        var footerPadding = new Padding(_px(ContentPadding), _px(10), _px(ContentPadding), _px(12));
        var footer = new Panel
        {
            Name = "footer",
            Dock = DockStyle.Bottom,
            Height = buttonRow.GetPreferredSize(Size.Empty).Height + footerPadding.Vertical,
            Padding = footerPadding,
            BackColor = ThemeHelper.Colors.GetPanelBackgroundColor()
        };
        footer.Controls.Add(buttonRow);

        CancelButton = closeButton;
        // Docking starts from the control added last: the footer takes the bottom, the page fills the rest.
        Controls.Add(_scrollPanel);
        Controls.Add(footer);
        ActiveControl = closeButton;

        Rebuild();
        ResumeLayout(true);

        _host.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>Rebuilds the page from fresh facts. Keeps the scroll position.</summary>
    internal void Rebuild()
    {
        if (IsDisposed)
        {
            return;
        }

        var context = _host.GetSecurityContext();
        var settings = _host.Settings.Current;
        var scroll = _scrollPanel.AutoScrollPosition;

        _scrollPanel.SuspendLayout();
        _content.SuspendLayout();
        ClearContent();
        BuildContent(settings, context);
        _content.ResumeLayout(true);
        UpdateWidths();
        _scrollPanel.ResumeLayout(true);
        _scrollPanel.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        Rebuild();
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (_content is not null)
        {
            _content.SuspendLayout();
            UpdateWidths();
            _content.ResumeLayout(true);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // Lengths were converted for the old DPI; convert them again for the new monitor.
        _px = DialogLayout.Scaler(this);
        _content.Location = new Point(_px(ContentPadding) + _scrollPanel.AutoScrollPosition.X,
            _px(ContentPadding) + _scrollPanel.AutoScrollPosition.Y);
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _host.Settings.Changed -= OnSettingsChanged;
        }

        base.Dispose(disposing);
        if (disposing)
        {
            DisposeContentResources();
            _boldFont.Dispose();
            _sectionFont.Dispose();
            _baseFont.Dispose();
            _icon?.Dispose();
        }
    }

    private void OnSettingsChanged(ApplicationSettings settings) => Rebuild();

    private void BuildContent(ApplicationSettings settings, SecurityContext context)
    {
        var px = _px;
        var width = ContentWidth();

        // What needs attention comes first, so it is visible without scrolling; the background follows.
        AddSection("Things to know", first: true);
        var risks = SecurityPosture.Evaluate(settings, context);
        if (risks.Count == 0)
        {
            AddText(NothingToReportText, width, "nothingToReportLabel");
        }
        else
        {
            var labels = new List<Label>();
            var rows = RiskDialog.CreateRows(risks, px, width, _boldFont, _contentResources, labels);
            DialogLayout.AddRow(_content, rows, Padding.Empty);
            var indent = width - RiskDialog.TextColumnWidth(px, width);
            foreach (var label in labels)
            {
                _widthTracked.Add((label, indent));
            }
        }

        AddSection("Right now", first: false);
        AddText($"Running as administrator: {YesNo(context.IsElevated)}", width, "elevatedLabel");
        AddText("Program folder:", width, null, bottom: px(2));
        AddValueBox(Show(context.Install.Directory), width, "programFolderBox", "Program folder");
        AddText(context.Install.Explanation, width, "installExplanationLabel");
        AddText("Settings file:", width, null, bottom: px(2));
        AddValueBox(Show(context.SettingsPath), width, "settingsPathBox", "Settings file");
        AddText($"Starts with Windows: {YesNo(context.StartsWithWindows)}", width, "startsWithWindowsLabel");

        AddSection("What WinXCornersPlus does", first: false);
        foreach (var fact in SecurityPosture.Facts(context))
        {
            AddBullet(fact, width);
        }
    }

    private void AddSection(string text, bool first)
    {
        var label = DialogLayout.CreateWrappingLabel(text, ContentWidth());
        label.Font = _sectionFont;
        DialogLayout.AddRow(_content, label, new Padding(0, first ? 0 : _px(18), 0, _px(8)));
        _widthTracked.Add((label, 0));
    }

    private void AddText(string text, int width, string? name, int? bottom = null)
    {
        var label = DialogLayout.CreateWrappingLabel(text, width, name);
        DialogLayout.AddRow(_content, label, new Padding(0, 0, 0, bottom ?? _px(8)));
        _widthTracked.Add((label, 0));
    }

    private void AddBullet(string text, int width)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, _px(16)));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var bullet = DialogLayout.CreateWrappingLabel("•", _px(16));
        var label = DialogLayout.CreateWrappingLabel(text, width - _px(16));
        row.Controls.Add(bullet, 0, 0);
        row.Controls.Add(label, 1, 0);
        DialogLayout.AddRow(_content, row, new Padding(0, 0, 0, _px(6)));
        _widthTracked.Add((label, _px(16)));
    }

    // Paths go in a read-only text box rather than a label, so the user can select and copy them. It wraps (a long
    // path has no spaces, so it breaks between characters) and is as tall as its text, so nothing is cut off.
    private void AddValueBox(string text, int width, string name, string accessibleName)
    {
        var box = new TextBox
        {
            Name = name,
            AccessibleName = accessibleName,
            Text = text,
            ReadOnly = true,
            Multiline = true,
            WordWrap = true,
            ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.None,
            BackColor = BackColor,
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            Width = width
        };
        FitValueBoxHeight(box);
        DialogLayout.AddRow(_content, box, new Padding(0, 0, 0, _px(8)));
        _widthTracked.Add((box, 0));
    }

    private static void FitValueBoxHeight(TextBox box)
    {
        // The edit control keeps a few pixels of margin on each side, so it wraps a little narrower than its width.
        var measured = TextRenderer.MeasureText(box.Text.Length == 0 ? " " : box.Text, box.Font,
            new Size(Math.Max(1, box.Width - 8), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
        box.Height = measured.Height + 2;
    }

    private int ContentWidth()
    {
        // The scroll bar's room is always reserved so the text does not re-wrap when it appears.
        var width = ClientSize.Width - 2 * _px(ContentPadding) - DialogLayout.ScrollBarWidth(this);
        return Math.Max(_px(200), width);
    }

    private void UpdateWidths()
    {
        var width = ContentWidth();
        foreach (var (control, indent) in _widthTracked)
        {
            var available = Math.Max(_px(80), width - indent);
            if (control is Label label)
            {
                label.MaximumSize = new Size(available, 0);
            }
            else
            {
                control.Width = available;
                if (control is TextBox box)
                {
                    FitValueBoxHeight(box);
                }
            }
        }
    }

    private void ClearContent()
    {
        _widthTracked.Clear();
        var old = _content.Controls.Cast<Control>().ToList();
        _content.Controls.Clear();
        _content.RowStyles.Clear();
        _content.RowCount = 0;
        foreach (var control in old)
        {
            control.Dispose();
        }

        DisposeContentResources();
    }

    private void DisposeContentResources()
    {
        foreach (var resource in _contentResources)
        {
            resource.Dispose();
        }

        _contentResources.Clear();
    }

    private static string Show(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(unknown)" : DisplayText.ForDisplay(value, maxLength: 260);

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
