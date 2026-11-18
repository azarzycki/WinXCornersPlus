using WinXCorners.Core.Layout;

namespace WinXCorners.App;

/// <summary>
/// DPI scaling for the two windows that keep their hand-placed, 96-DPI layout (Settings and About).
/// Captures the form's client size and every child control's bounds once, then re-applies them scaled
/// for the window's current DPI. Fonts are not touched: they are in points and Windows already sizes them.
/// This replaces the three copies of the same code the forms used to carry (review M4).
/// New dialogs use auto-layout instead and must not use this class.
/// </summary>
internal sealed class UiScaling
{
    /// <summary>WM_DPICHANGED: forward it to <see cref="Apply"/> from the form's WndProc.</summary>
    internal const int WmDpiChanged = 0x02E0;

    private readonly Form _form;
    private readonly Size _baseClientSize;
    private readonly Dictionary<Control, Rectangle> _baseBounds = [];
    private double _factor = 1.0;

    /// <summary>Call after every control has been added and placed at its 96-DPI position.</summary>
    internal UiScaling(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        _form = form;
        _baseClientSize = form.ClientSize;
        foreach (var control in Descendants(form))
        {
            _baseBounds[control] = control.Bounds;
        }
    }

    /// <summary>Re-applies the captured layout for the current DPI; does nothing if the factor has not changed.</summary>
    internal void Apply()
    {
        var factor = GetScaleFactor(_form);
        if (Math.Abs(factor - _factor) < 0.01)
        {
            return;
        }

        _factor = factor;
        _form.ClientSize = DpiScale.Scale(_baseClientSize, factor);
        foreach (var (control, bounds) in _baseBounds)
        {
            if (!control.IsDisposed)
            {
                control.Bounds = DpiScale.Scale(bounds, factor);
            }
        }
    }

    /// <summary>
    /// The window's DPI / 96. Before the window has a handle (or on Windows versions without
    /// GetDpiForWindow) the screen DPI is used, without forcing the handle to be created.
    /// </summary>
    internal static double GetScaleFactor(Control control)
    {
        if (control.IsHandleCreated)
        {
            try
            {
                var dpi = NativeMethods.GetDpiForWindow(control.Handle);
                if (dpi > 0)
                {
                    return DpiScale.FactorFromDpi(dpi);
                }
            }
            catch (EntryPointNotFoundException)
            {
                // Windows 10 before 1607: fall through to the screen DPI.
            }
        }

        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return DpiScale.FactorFromDpi((int)Math.Round(graphics.DpiX));
    }

    private static List<Control> Descendants(Control root)
    {
        var result = new List<Control>();
        var stack = new Stack<Control>();
        foreach (Control child in root.Controls)
        {
            stack.Push(child);
        }

        while (stack.Count > 0)
        {
            var control = stack.Pop();
            result.Add(control);
            foreach (Control child in control.Controls)
            {
                stack.Push(child);
            }
        }

        return result;
    }
}
