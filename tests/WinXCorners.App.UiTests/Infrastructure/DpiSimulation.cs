namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>
/// Simulates a higher display scale on the 96-DPI test machine for the two hand-placed windows (Settings, About):
/// <see cref="UiScaling.ApplyFactor"/> scales the layout as WM_DPICHANGED would, and every font is enlarged by the
/// same factor, which is what Windows does to point-sized fonts at that scale.
/// </summary>
internal sealed class DpiSimulation : IDisposable
{
    private readonly List<Font> _fonts = [];

    private DpiSimulation(Form form, UiScaling scaling, double factor)
    {
        // Fonts first, explicit ones only (a control without its own font inherits the form's).
        var explicitFonts = Find.Descendants(form)
            .Where(static c => c.Parent is not null && !ReferenceEquals(c.Font, c.Parent.Font))
            .Select(static c => (Control: c, c.Font))
            .ToList();

        form.Font = Scaled(form.Font, factor);
        foreach (var (control, font) in explicitFonts)
        {
            control.Font = Scaled(font, factor);
        }

        scaling.ApplyFactor(factor);
        form.PerformLayout();
        Ui.Pump(100);
    }

    /// <summary>
    /// Skips the test when the scaled window cannot fit on this machine's screen: Windows will not make a window
    /// larger than the screen, so its layout at that scale cannot be checked here.
    /// </summary>
    internal static void SkipIfScreenTooSmall(Form form, Size wantedClientSize)
    {
        var screen = Screen.FromControl(form).Bounds;
        Skip.If(form.ClientSize.Width < wantedClientSize.Width || form.ClientSize.Height < wantedClientSize.Height,
            $"The screen ({screen.Width}x{screen.Height}) is too small for a {wantedClientSize.Width}x{wantedClientSize.Height} window at this scale.");
    }

    internal static DpiSimulation Apply(Form form, UiScaling scaling, double factor) => new(form, scaling, factor);

    public void Dispose()
    {
        // The forms are disposed by then; their fonts were replaced with these.
        foreach (var font in _fonts)
        {
            font.Dispose();
        }
    }

    private Font Scaled(Font font, double factor)
    {
        var scaled = new Font(font.FontFamily, (float)(font.SizeInPoints * factor), font.Style, GraphicsUnit.Point);
        _fonts.Add(scaled);
        return scaled;
    }
}
