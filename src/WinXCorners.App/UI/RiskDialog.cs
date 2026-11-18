using WinXCorners.Core.Security;

namespace WinXCorners.App;

/// <summary>
/// The "eyes wide open" dialog (brief design point 5): shows the risks of what the user is about to turn
/// on, in Core's words, and makes consent a deliberate click. Cancel is the default button, has focus and
/// is what Enter, Esc and the title-bar close do.
/// </summary>
internal static class RiskDialog
{
    /// <summary>Width of the dialog's content in 96-DPI pixels.</summary>
    internal const int ContentWidth = 520;

    /// <summary>Gap between the severity icon and the text column, in 96-DPI pixels.</summary>
    private const int IconGap = 10;

    /// <summary>Severity icon size in 96-DPI pixels.</summary>
    private const int IconSize = 22;

    /// <summary>
    /// Asks the user to confirm. True only when the confirm button was clicked; also true, without showing
    /// anything, when <paramref name="items"/> is empty (there is nothing to warn about).
    /// </summary>
    internal static bool Confirm(IWin32Window? owner, string title, string heading, IReadOnlyList<RiskItem> items,
        string confirmText, string cancelText = "Cancel")
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return true;
        }

        using var dialog = CreateConfirm(title, heading, items, confirmText, cancelText);
        return ConsentForm.ShowModal(dialog, owner) == DialogResult.OK;
    }

    /// <summary>Shows the items with a single Close button. Does nothing when <paramref name="items"/> is empty.</summary>
    internal static void Inform(IWin32Window? owner, string title, string heading, IReadOnlyList<RiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return;
        }

        using var dialog = CreateInform(title, heading, items);
        ConsentForm.ShowModal(dialog, owner);
    }

    /// <summary>Builds the confirmation dialog without showing it (for <see cref="Confirm"/> and for tests).</summary>
    internal static Form CreateConfirm(string title, string heading, IReadOnlyList<RiskItem> items,
        string confirmText, string cancelText = "Cancel")
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentException.ThrowIfNullOrEmpty(confirmText);
        return new ConsentForm(title, heading, confirmText, cancelText, form => CreateBody(form, items));
    }

    /// <summary>Builds the information-only dialog without showing it (for <see cref="Inform"/> and for tests).</summary>
    internal static Form CreateInform(string title, string heading, IReadOnlyList<RiskItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new ConsentForm(title, heading, confirmText: null, cancelText: "Close", form => CreateBody(form, items));
    }

    /// <summary>
    /// Builds one row per item: a severity icon, the title in bold and the detail wrapped beneath it.
    /// Shared by every window that lists risks so they all look the same. The table is named "riskList".
    /// </summary>
    /// <param name="px">Converts a 96-DPI length to device pixels for the hosting window.</param>
    /// <param name="width">Total width of the rows, already converted.</param>
    /// <param name="boldFont">Owned by the caller.</param>
    /// <param name="resources">Receives the bitmaps created here; the caller disposes them.</param>
    /// <param name="textLabels">Optional: receives every text label, for callers that re-wrap on resize.</param>
    internal static TableLayoutPanel CreateRows(IReadOnlyList<RiskItem> items, Func<int, int> px, int width,
        Font boldFont, ICollection<IDisposable> resources, ICollection<Label>? textLabels = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(px);
        ArgumentNullException.ThrowIfNull(boldFont);
        ArgumentNullException.ThrowIfNull(resources);

        var table = new TableLayoutPanel
        {
            Name = "riskList",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
            AccessibleRole = AccessibleRole.List
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var textWidth = TextColumnWidth(px, width);
        var row = 0;
        foreach (var item in items)
        {
            var bitmap = SeverityBitmap(item.Severity);
            resources.Add(bitmap);
            var icon = new PictureBox
            {
                Name = "riskIcon" + row / 2,
                Image = bitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(px(IconSize), px(IconSize)),
                Margin = new Padding(0, px(1), px(IconGap), 0),
                BackColor = Color.Transparent,
                AccessibleName = SeverityName(item.Severity),
                AccessibleRole = AccessibleRole.Graphic
            };

            var title = DialogLayout.CreateWrappingLabel(item.Title, textWidth, "riskTitle" + row / 2);
            title.Font = boldFont;
            title.Margin = new Padding(0, px(2), 0, px(2));

            var detail = DialogLayout.CreateWrappingLabel(item.Detail, textWidth, "riskDetail" + row / 2);
            detail.Margin = new Padding(0, 0, 0, px(12));

            textLabels?.Add(title);
            textLabels?.Add(detail);

            table.RowCount = row + 2;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(icon, 0, row);
            table.SetRowSpan(icon, 2);
            table.Controls.Add(title, 1, row);
            table.Controls.Add(detail, 1, row + 1);
            row += 2;
        }

        return table;
    }

    /// <summary>The wrap width of the text column of rows that are <paramref name="width"/> wide.</summary>
    internal static int TextColumnWidth(Func<int, int> px, int width) => Math.Max(px(80), width - px(IconSize) - px(IconGap));

    private static Control CreateBody(ConsentForm form, IReadOnlyList<RiskItem> items)
    {
        var rows = CreateRows(items, form.Px, form.ContentPixelWidth, form.BoldFont, form.Resources);
        return DialogLayout.CreateScroller(rows, form.ContentPixelWidth, DialogLayout.WorkingAreaHeight(0.7),
            DialogLayout.ScrollBarWidth(form), "riskScroller");
    }

    private static Bitmap SeverityBitmap(RiskSeverity severity)
    {
        // SystemIcons.* are shared, cached instances: never dispose them. ToBitmap() returns a new bitmap we own.
        var icon = severity switch
        {
            RiskSeverity.Warning => SystemIcons.Warning,
            RiskSeverity.Caution => SystemIcons.Shield,
            _ => SystemIcons.Information
        };
        return icon.ToBitmap();
    }

    private static string SeverityName(RiskSeverity severity) => severity switch
    {
        RiskSeverity.Warning => "Warning",
        RiskSeverity.Caution => "Caution",
        _ => "Information"
    };
}

/// <summary>
/// The fixed-width, auto-height dialog shared by <see cref="RiskDialog"/> and <see cref="ElevatedApprovalDialog"/>:
/// a bold heading, a body built by the caller, and buttons at the bottom right. The cancel button is the
/// default and the Esc button and has the initial focus, so consent always takes a deliberate click on
/// "confirmButton". DialogResult.OK means exactly that click.
/// </summary>
internal sealed class ConsentForm : Form
{
    private readonly List<IDisposable> _resources = [];
    private readonly Font _baseFont;
    private readonly Font _headingFont;
    private readonly Icon? _icon;
    private readonly Button _cancelButton;

    internal ConsentForm(string title, string heading, string? confirmText, string cancelText,
        Func<ConsentForm, Control> buildBody, bool showInTaskbar = false)
    {
        ArgumentNullException.ThrowIfNull(buildBody);
        SuspendLayout();

        Name = "consentForm";
        Text = title;
        AutoScaleMode = AutoScaleMode.None; // lengths are converted with Px below
        _baseFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        Font = _baseFont;
        _headingFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
        BoldFont = new Font(_baseFont, FontStyle.Bold);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = showInTaskbar;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _icon = WindowIconLoader.TryLoadAppIcon();
        if (_icon is not null)
        {
            Icon = _icon;
        }

        DialogLayout.ApplyTheme(this);

        Px = DialogLayout.Scaler(this);
        ContentPixelWidth = Px(RiskDialog.ContentWidth);

        var root = DialogLayout.CreateColumn("rootLayout");
        root.Padding = new Padding(Px(20), Px(16), Px(20), Px(16));

        var headingLabel = DialogLayout.CreateWrappingLabel(heading, ContentPixelWidth, "headingLabel");
        headingLabel.Font = _headingFont;
        DialogLayout.AddRow(root, headingLabel, new Padding(0, 0, 0, Px(14)));

        var body = buildBody(this);
        DialogLayout.AddRow(root, body, new Padding(0, 0, 0, Px(8)));

        _cancelButton = DialogLayout.CreateButton(cancelText, "cancelButton", Px);
        _cancelButton.DialogResult = DialogResult.Cancel;
        Button[] buttons;
        if (confirmText is null)
        {
            buttons = [_cancelButton];
        }
        else
        {
            var confirmButton = DialogLayout.CreateButton(confirmText, "confirmButton", Px);
            confirmButton.DialogResult = DialogResult.OK;
            buttons = [confirmButton, _cancelButton];
        }

        var buttonRow = DialogLayout.CreateButtonRow(buttons);
        DialogLayout.AddRow(root, buttonRow, new Padding(0, Px(8), 0, 0));
        buttonRow.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        // The layout must be part of the form before a control in it can become the active control: setting
        // ActiveControl first threw "Invisible or disabled control cannot be activated", which crashed every
        // consent dialog, including the elevated-start approval (found by the Windows CI smoke test).
        Controls.Add(root);

        // Consent must be a deliberate click: Enter and Esc both mean "cancel".
        AcceptButton = _cancelButton;
        CancelButton = _cancelButton;
        ActiveControl = _cancelButton;
        ResumeLayout(true);
    }

    /// <summary>Converts 96-DPI lengths to device pixels for this window.</summary>
    internal Func<int, int> Px { get; }

    /// <summary>The content width (about 520 logical pixels) in device pixels.</summary>
    internal int ContentPixelWidth { get; }

    /// <summary>Bold body font; owned by this form.</summary>
    internal Font BoldFont { get; }

    /// <summary>Anything the body creates that must be disposed with the form (bitmaps, fonts).</summary>
    internal ICollection<IDisposable> Resources => _resources;

    /// <summary>Shows the dialog modally, centred on <paramref name="owner"/> or, without one, on the screen.</summary>
    internal static DialogResult ShowModal(Form dialog, IWin32Window? owner)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (owner is null)
        {
            dialog.StartPosition = FormStartPosition.CenterScreen;
            return dialog.ShowDialog();
        }

        return dialog.ShowDialog(owner);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _cancelButton.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var resource in _resources)
            {
                resource.Dispose();
            }

            _resources.Clear();
            BoldFont.Dispose();
            _headingFont.Dispose();
            _baseFont.Dispose();
            _icon?.Dispose();
        }
    }
}
