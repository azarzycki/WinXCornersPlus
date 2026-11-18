using System.Text;

namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>
/// Structural checks of a shown window: what a user would see as overlapping, cut-off or unlabelled controls.
/// Only visible controls are checked, so call it after the window has been shown and laid out.
/// </summary>
internal static class LayoutAssert
{
    /// <summary>Pixels of rounding slack allowed when comparing text extents with control sizes.</summary>
    private const int Tolerance = 1;

    /// <summary>
    /// Controls that draw their text themselves and shorten it on purpose, with the full text available elsewhere.
    /// Keep this list short and justified.
    /// </summary>
    private static readonly HashSet<string> EllipsizedByDesign =
    [
        "settingsPathLabel" // About: path shortened in the middle (PathEllipsis); the full path is in its tooltip.
    ];

    /// <summary>Fails with every problem found under <paramref name="root"/>.</summary>
    internal static void IsSound(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Assert.True(root.Visible, $"{Describe(root)} must be shown before its layout is checked.");
        root.PerformLayout();

        var problems = new List<string>();
        Check(root, problems);
        if (problems.Count > 0)
        {
            var message = new StringBuilder($"Layout problems in {Describe(root)}:");
            foreach (var problem in problems)
            {
                message.AppendLine().Append("  - ").Append(problem);
            }

            Assert.Fail(message.ToString());
        }
    }

    private static void Check(Control parent, List<string> problems)
    {
        var children = parent.Controls.Cast<Control>().Where(static c => c.Visible).ToList();
        var scrolls = parent is ScrollableControl { AutoScroll: true };
        var client = parent.ClientRectangle;

        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];

            for (var j = i + 1; j < children.Count; j++)
            {
                var other = children[j];
                var overlap = Rectangle.Intersect(child.Bounds, other.Bounds);
                if (overlap.Width > 0 && overlap.Height > 0)
                {
                    problems.Add($"{Describe(child)} {child.Bounds} overlaps {Describe(other)} {other.Bounds}");
                }
            }

            if (!scrolls && !client.Contains(child.Bounds))
            {
                problems.Add($"{Describe(child)} {child.Bounds} lies outside its parent {Describe(parent)} client area {client}");
            }

            CheckText(child, problems);
            CheckAccessibleName(child, problems);
            Check(child, problems);
        }
    }

    private static void CheckText(Control control, List<string> problems)
    {
        if (control is not (Label or CheckBox or Button or LinkLabel) || string.IsNullOrEmpty(control.Text))
        {
            return;
        }

        if (control is Label { AutoEllipsis: true } || EllipsizedByDesign.Contains(control.Name))
        {
            return;
        }

        var needed = NeededSize(control);
        var available = control.ClientSize;
        if (needed.Width > available.Width + Tolerance || needed.Height > available.Height + Tolerance)
        {
            problems.Add($"{Describe(control)} text '{Shorten(control.Text)}' needs {needed.Width}x{needed.Height} but has {available.Width}x{available.Height} (clipped)");
        }
    }

    /// <summary>The size the control needs to show its whole text.</summary>
    private static Size NeededSize(Control control)
    {
        switch (control)
        {
            case Label label:
            {
                // Labels wrap at their width; what matters is whether the wrapped text fits the height, and whether a
                // single word is wider than the label.
                var flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl;
                if (!label.UseMnemonic)
                {
                    flags |= TextFormatFlags.NoPrefix;
                }

                var width = Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal);
                var measured = TextRenderer.MeasureText(label.Text, label.Font, new Size(width, int.MaxValue), flags);
                return new Size(measured.Width + label.Padding.Horizontal, measured.Height + label.Padding.Vertical);
            }

            case CheckBox checkBox:
                // PreferredSize is glyph + gap + single-line text, which is how the check box draws it.
                return checkBox.GetPreferredSize(Size.Empty);

            default:
            {
                // Buttons draw one line, centred; keep a few pixels for the border.
                var text = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine);
                return new Size(text.Width + 4, text.Height + 2);
            }
        }
    }

    private static void CheckAccessibleName(Control control, List<string> problems)
    {
        if (!control.TabStop || !control.CanSelect)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(control.AccessibleName) && string.IsNullOrWhiteSpace(control.Text))
        {
            problems.Add($"{Describe(control)} takes the focus but has no accessible name or text");
        }
    }

    private static string Describe(Control control) =>
        string.IsNullOrEmpty(control.Name) ? $"<{control.GetType().Name}>" : $"{control.Name} ({control.GetType().Name})";

    private static string Shorten(string text)
    {
        var oneLine = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ');
        return oneLine.Length <= 60 ? oneLine : oneLine[..57] + "...";
    }
}
