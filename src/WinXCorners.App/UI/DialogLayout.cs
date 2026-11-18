using WinXCorners.Core.Layout;

namespace WinXCorners.App;

/// <summary>
/// Building blocks for the auto-layout windows (risk dialogs, administrator approval, Security page).
/// Lengths are written in 96-DPI logical pixels and converted once with <see cref="Scaler"/> for the DPI the
/// window is created at; text sizes itself because fonts are in points. These windows deliberately do not use
/// <see cref="UiScaling"/>, which exists only for the two hand-placed legacy windows.
/// </summary>
internal static class DialogLayout
{
    /// <summary>Converts a 96-DPI length to device pixels for <paramref name="control"/>'s current DPI.</summary>
    internal static Func<int, int> Scaler(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var factor = DpiScale.FactorFromDpi(control.DeviceDpi);
        return value => DpiScale.Scale(value, factor);
    }

    /// <summary>Width of a vertical scroll bar at <paramref name="control"/>'s current DPI.</summary>
    internal static int ScrollBarWidth(Control control) =>
        SystemInformation.GetVerticalScrollBarWidthForDpi(control.DeviceDpi);

    /// <summary>The given fraction of the height of the working area under the pointer.</summary>
    internal static int WorkingAreaHeight(double fraction)
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        return Math.Max(1, (int)(area.Height * fraction));
    }

    /// <summary>Background, text colour and native (dark/light) title bar, like the other windows.</summary>
    internal static void ApplyTheme(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.BackColor = ThemeHelper.Colors.GetBackgroundColor();
        form.ForeColor = ThemeHelper.Colors.GetForegroundColor();
        form.HandleCreated += static (sender, _) => ThemeHelper.ApplyNativeWindowTheme(((Form)sender!).Handle);
    }

    /// <summary>
    /// A label that wraps at <paramref name="maxWidth"/> and grows in height to fit. Mnemonics are off because
    /// the text is data, not a caption.
    /// </summary>
    internal static Label CreateWrappingLabel(string text, int maxWidth, string? name = null)
    {
        return new Label
        {
            Name = name ?? string.Empty,
            Text = text,
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(Math.Max(1, maxWidth), 0),
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            Margin = Padding.Empty,
            BackColor = Color.Transparent,
            ForeColor = ThemeHelper.Colors.GetForegroundColor()
        };
    }

    /// <summary>A push button sized to its text, at least 88 x 26 logical pixels.</summary>
    internal static Button CreateButton(string text, string name, Func<int, int> px)
    {
        return new SettingsFooterButton
        {
            Name = name,
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(px(88), px(26)),
            Padding = new Padding(px(8), 0, px(8), 0),
            Margin = new Padding(px(8), 0, 0, 0)
        };
    }

    /// <summary>Buttons in a row aligned to the right; <paramref name="buttons"/> are given left to right.</summary>
    internal static FlowLayoutPanel CreateButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel
        {
            Name = "buttonRow",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };

        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].TabIndex = i;
            row.Controls.Add(buttons[i]);
        }

        return row;
    }

    /// <summary>
    /// Hosts <paramref name="content"/> (an auto-sizing control) at its natural height, or at
    /// <paramref name="maxHeight"/> with a vertical scroll bar when it is taller than that.
    /// </summary>
    internal static Panel CreateScroller(Control content, int width, int maxHeight, int scrollBarWidth, string name)
    {
        ArgumentNullException.ThrowIfNull(content);
        var preferred = content.GetPreferredSize(Size.Empty);
        var scrolls = preferred.Height > maxHeight;
        var panel = new Panel
        {
            Name = name,
            AutoScroll = scrolls,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent,
            Size = new Size(
                Math.Max(width, preferred.Width) + (scrolls ? scrollBarWidth : 0),
                scrolls ? maxHeight : preferred.Height)
        };
        content.Location = Point.Empty;
        panel.Controls.Add(content);
        return panel;
    }

    /// <summary>
    /// A read-only, wrapping, multi-line text box sized to its text up to <paramref name="maxHeight"/>, with a
    /// scroll bar beyond that. Used for lists the user must be able to read in full and copy.
    /// </summary>
    internal static TextBox CreateReadOnlyList(IReadOnlyList<string> lines, Font font, int width, int maxHeight,
        int scrollBarWidth, string name, Func<int, int> px)
    {
        var text = string.Join(Environment.NewLine, lines);

        // Measured at the width the edit control wraps at (its border and inner margins take about 10 px); the
        // scroll bar's width is taken off only when the text does not fit and a scroll bar will be shown.
        int Measure(int wrapWidth) => TextRenderer.MeasureText(text.Length == 0 ? " " : text, font,
            new Size(Math.Max(1, wrapWidth), int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height + px(8);

        var wanted = Measure(width - px(10));
        var scrolls = wanted > maxHeight;
        if (scrolls)
        {
            wanted = Measure(width - px(10) - scrollBarWidth);
        }

        return new TextBox
        {
            Name = name,
            Multiline = true,
            ReadOnly = true,
            WordWrap = true,
            ScrollBars = scrolls ? ScrollBars.Vertical : ScrollBars.None,
            Text = text,
            Font = font,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeHelper.Colors.GetControlBackgroundColor(),
            ForeColor = ThemeHelper.Colors.GetForegroundColor(),
            Size = new Size(width, Math.Min(wanted, maxHeight)),
            Margin = Padding.Empty
        };
    }

    /// <summary>A single-column table that sizes itself to its rows.</summary>
    internal static TableLayoutPanel CreateColumn(string name)
    {
        var table = new TableLayoutPanel
        {
            Name = name,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return table;
    }

    /// <summary>Appends <paramref name="control"/> as a new auto-sized row of a single-column table.</summary>
    internal static void AddRow(TableLayoutPanel table, Control control, Padding margin)
    {
        control.Margin = margin;
        table.RowCount += 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(control, 0, table.RowCount - 1);
    }
}
