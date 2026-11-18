using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WinXCorners.App.UiTests.Infrastructure;

/// <summary>
/// Saves what a window looks like to TestResults/screenshots (the workflow uploads that folder). Two images per call:
/// <c>&lt;name&gt;.png</c> from DrawToBitmap (always works, even without a visible desktop; the title bar is drawn
/// the classic way) and <c>&lt;name&gt;.screen.png</c> copied from the screen (exactly what the user sees, including
/// the dark title bar), when the desktop can be captured.
/// </summary>
internal static class Screenshot
{
    internal static readonly Point DefaultLocation = new(40, 40);

    internal static string Directory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR");
            var directory = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "TestResults", "screenshots")
                : configured;
            System.IO.Directory.CreateDirectory(directory);
            return directory;
        }
    }

    /// <summary>
    /// Shows <paramref name="form"/> (at <paramref name="location"/> when it is not yet visible), pumps until it has
    /// painted, and saves it. Returns the path of the DrawToBitmap image.
    /// </summary>
    internal static string Capture(Form form, string name, Point? location = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!form.Visible)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = location ?? DefaultLocation;
            form.Show();
        }

        // On top for the screen copy, without stealing activation rules from the window itself.
        var wasTopMost = form.TopMost;
        form.TopMost = true;
        form.Refresh();
        Ui.Pump(250);

        var directory = Directory;
        var path = Path.Combine(directory, name + ".png");
        using (var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height)))
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            if (IsBlank(bitmap))
            {
                using var fromScreen = CopyFromScreen(form);
                fromScreen.Save(path, ImageFormat.Png);
            }
            else
            {
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        try
        {
            // PrintWindow with PW_RENDERFULLCONTENT renders the window as DWM composes it (dark title bar, rounded
            // corners) without depending on what is on top of it; the Windows 11 ARM64 runner's screen copies do
            // not show application windows at all. A plain screen copy is the fallback.
            using var composed = PrintWindowImage(form);
            using var screen = composed is null || IsBlank(composed) ? CopyFromScreen(form) : null;
            var image = screen ?? composed;
            if (image is not null && !IsBlank(image))
            {
                image.Save(Path.Combine(directory, name + ".screen.png"), ImageFormat.Png);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException)
        {
            // No interactive desktop: the DrawToBitmap image is all there is.
            File.WriteAllText(Path.Combine(directory, name + ".screen.txt"), "Screen capture failed: " + ex.Message);
        }

        form.TopMost = wasTopMost;
        return path;
    }

    /// <summary>The window's on-screen rectangle as DWM draws it (without the invisible resize borders).</summary>
    private static Rectangle VisibleBounds(Form form)
    {
        if (DwmGetWindowAttribute(form.Handle, DwmwaExtendedFrameBounds, out var rect, Marshal.SizeOf<Rect>()) == 0)
        {
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        return form.Bounds;
    }

    private static Bitmap? PrintWindowImage(Form form)
    {
        if (!GetWindowRect(form.Handle, out var window))
        {
            return null;
        }

        var width = Math.Max(1, window.Right - window.Left);
        var height = Math.Max(1, window.Bottom - window.Top);
        using var full = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(full))
        {
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(form.Handle, hdc, PwRenderFullContent))
                {
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }

        // Crop the invisible resize borders, as the user sees the window.
        var visible = VisibleBounds(form);
        var crop = Rectangle.Intersect(new Rectangle(0, 0, width, height),
            new Rectangle(visible.Left - window.Left, visible.Top - window.Top, visible.Width, visible.Height));
        return crop.Width > 0 && crop.Height > 0 ? full.Clone(crop, full.PixelFormat) : null;
    }

    private static Bitmap CopyFromScreen(Form form)
    {
        var bounds = VisibleBounds(form);
        var bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }

    /// <summary>True when every sampled pixel has the same colour (nothing was drawn).</summary>
    private static bool IsBlank(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);
        var stepX = Math.Max(1, bitmap.Width / 40);
        var stepY = Math.Max(1, bitmap.Height / 40);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                if (bitmap.GetPixel(x, y) != first)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const uint PwRenderFullContent = 0x00000002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
